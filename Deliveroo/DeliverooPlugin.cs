using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Plugin;
using Deliveroo.External;
using Deliveroo.GameData;
using Deliveroo.Windows;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;
using Condition = Dalamud.Game.ClientState.Conditions.Condition;

namespace Deliveroo;

public sealed partial class DeliverooPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(typeof(DeliverooPlugin).AssemblyQualifiedName);

    private readonly DalamudPluginInterface _pluginInterface;
    private readonly ChatGui _chatGui;
    private readonly GameGui _gameGui;
    private readonly Framework _framework;
    private readonly ClientState _clientState;
    private readonly ObjectTable _objectTable;
    private readonly TargetManager _targetManager;
    private readonly Condition _condition;
    private readonly CommandManager _commandManager;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Configuration _configuration;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly YesAlreadyIpc _yesAlreadyIpc;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly GcRewardsCache _gcRewardsCache;

    private readonly ConfigWindow _configWindow;
    private readonly TurnInWindow _turnInWindow;
    private readonly IReadOnlyDictionary<uint, uint> _sealCaps;

    private Stage _currentStageInternal = Stage.Stopped;
    private DateTime _continueAt = DateTime.MinValue;
    private List<PurchaseItemRequest> _itemsToPurchaseNow = new();
    private (bool Saved, bool? PreviousState) _yesAlreadyState = (false, null);

    public DeliverooPlugin(DalamudPluginInterface pluginInterface, ChatGui chatGui, GameGui gameGui,
        Framework framework, ClientState clientState, ObjectTable objectTable, TargetManager targetManager,
        DataManager dataManager, Condition condition, CommandManager commandManager)
    {
        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _targetManager = targetManager;
        _condition = condition;
        _commandManager = commandManager;

        var dalamudReflector = new DalamudReflector(_pluginInterface, _framework);
        _yesAlreadyIpc = new YesAlreadyIpc(dalamudReflector);
        _configuration = (Configuration?)_pluginInterface.GetPluginConfig() ?? new Configuration();
        _gcRewardsCache = new GcRewardsCache(dataManager);
        _configWindow = new ConfigWindow(_pluginInterface, this, _configuration, _gcRewardsCache, _clientState);
        _windowSystem.AddWindow(_configWindow);
        _turnInWindow = new TurnInWindow(this, _pluginInterface, _configuration, _gcRewardsCache, _configWindow);
        _windowSystem.AddWindow(_turnInWindow);
        _sealCaps = dataManager.GetExcelSheet<GrandCompanyRank>()!.Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId, x => x.MaxSeals);

        _framework.Update += FrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += _configWindow.Toggle;
        _clientState.Login += Login;
        _clientState.Logout += Logout;
        _commandManager.AddHandler("/deliveroo", new CommandInfo(ProcessCommand)
        {
            HelpMessage = "Open the configuration"
        });

        if (_clientState.IsLoggedIn)
            Login(this, EventArgs.Empty);

        if (_configuration.AddVentureIfNoItemToPurchaseSelected())
            _pluginInterface.SavePluginConfig(_configuration);
    }

    public string Name => "Deliveroo";

    internal CharacterConfiguration? CharacterConfiguration { get; set; }


    internal Stage CurrentStage
    {
        get => _currentStageInternal;
        set
        {
            if (_currentStageInternal != value)
            {
                PluginLog.Information($"Changing stage from {_currentStageInternal} to {value}");
                _currentStageInternal = value;
            }
        }
    }

    private void Login(object? sender, EventArgs e)
    {
        try
        {
            CharacterConfiguration = CharacterConfiguration.Load(_pluginInterface, _clientState.LocalContentId);
            if (CharacterConfiguration != null)
            {
                if (CharacterConfiguration.CachedPlayerName != _clientState.LocalPlayer!.Name.ToString() ||
                    CharacterConfiguration.CachedWorldName !=
                    _clientState.LocalPlayer.HomeWorld.GameData!.Name.ToString())
                {
                    CharacterConfiguration.CachedPlayerName = _clientState.LocalPlayer!.Name.ToString();
                    CharacterConfiguration.CachedWorldName =
                        _clientState.LocalPlayer.HomeWorld.GameData!.Name.ToString();

                    CharacterConfiguration.Save(_pluginInterface);
                }

                PluginLog.Information($"Loaded character-specific information for {_clientState.LocalContentId}");
            }
            else
            {
                PluginLog.Verbose(
                    $"No character-specific information for {_clientState.LocalContentId}");
            }
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, "Unable to load character configuration");
            CharacterConfiguration = null;
        }
    }

    private void Logout(object? sender, EventArgs e)
    {
        CharacterConfiguration = null;
    }

    private unsafe void FrameworkUpdate(Framework f)
    {
        _turnInWindow.Error = string.Empty;
        if (!_clientState.IsLoggedIn ||
            _clientState.TerritoryType is not 128 and not 130 and not 132 ||
            _condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            GetDistanceToNpc(GetQuartermasterId(), out GameObject? quartermaster) >= 7f ||
            GetDistanceToNpc(GetPersonnelOfficerId(), out GameObject? personnelOfficer) >= 7f ||
            CharacterConfiguration is { DisableForCharacter: true } ||
            _configWindow.IsOpen)
        {
            _turnInWindow.IsOpen = false;
            _turnInWindow.State = false;
            if (CurrentStage != Stage.Stopped)
            {
                RestoreYesAlready();
                CurrentStage = Stage.Stopped;
            }
        }
        else if (DateTime.Now > _continueAt)
        {
            _turnInWindow.IsOpen = true;
            _turnInWindow.Multiplier = GetSealMultiplier();

            if (!_turnInWindow.State)
            {
                if (CurrentStage != Stage.Stopped)
                {
                    RestoreYesAlready();
                    CurrentStage = Stage.Stopped;
                }

                return;
            }
            else if (_turnInWindow.State && CurrentStage == Stage.Stopped)
            {
                CurrentStage = Stage.TargetPersonnelOfficer;
                _itemsToPurchaseNow = _turnInWindow.SelectedItems;
                if (_itemsToPurchaseNow.Count > 0)
                {
                    PluginLog.Information("Items to purchase:");
                    foreach (var item in _itemsToPurchaseNow)
                        PluginLog.Information($"  {item.Name} (limit = {item.EffectiveLimit})");
                }
                else
                    PluginLog.Information("No items to purchase configured or available");


                var nextItem = GetNextItemToPurchase();
                if (nextItem != null && GetCurrentSealCount() >= _configuration.ReservedSealCount + nextItem.SealCost)
                    CurrentStage = Stage.TargetQuartermaster;

                if (TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList", out var gcSupplyList) &&
                    IsAddonReady(&gcSupplyList->AtkUnitBase))
                    CurrentStage = Stage.SelectExpertDeliveryTab;

                if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var gcExchange) &&
                    IsAddonReady(gcExchange))
                    CurrentStage = Stage.SelectRewardTier;
            }

            if (CurrentStage != Stage.Stopped && CurrentStage != Stage.RequestStop && !_yesAlreadyState.Saved)
                SaveYesAlready();

            switch (CurrentStage)
            {
                case Stage.TargetPersonnelOfficer:
                    InteractWithPersonnelOfficer(personnelOfficer!, quartermaster!);
                    break;

                case Stage.OpenGcSupply:
                    OpenGcSupply();
                    break;

                case Stage.SelectExpertDeliveryTab:
                    SelectExpertDeliveryTab();
                    break;

                case Stage.SelectItemToTurnIn:
                    SelectItemToTurnIn();
                    break;

                case Stage.TurnInSelected:
                    TurnInSelectedItem();
                    break;

                case Stage.FinalizeTurnIn:
                    FinalizeTurnInItem();
                    break;

                case Stage.CloseGcSupply:
                    CloseGcSupply();
                    break;

                case Stage.CloseGcSupplyThenStop:
                    CloseGcSupplyThenStop();
                    break;

                case Stage.TargetQuartermaster:
                    InteractWithQuartermaster(personnelOfficer!, quartermaster!);
                    break;

                case Stage.SelectRewardTier:
                    SelectRewardTier();
                    break;

                case Stage.SelectRewardSubCategory:
                    SelectRewardSubCategory();
                    break;

                case Stage.SelectReward:
                    SelectReward();
                    break;

                case Stage.ConfirmReward:
                    ConfirmReward();
                    break;

                case Stage.CloseGcExchange:
                    CloseGcExchange();
                    break;

                case Stage.RequestStop:
                    RestoreYesAlready();
                    CurrentStage = Stage.Stopped;

                    break;
                case Stage.Stopped:
                    break;
                default:
                    PluginLog.Warning($"Unknown stage {CurrentStage}");
                    break;
            }
        }
    }


    public void Dispose()
    {
        _commandManager.RemoveHandler("/deliveroo");
        _clientState.Logout -= Logout;
        _clientState.Login -= Login;
        _pluginInterface.UiBuilder.OpenConfigUi -= _configWindow.Toggle;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _framework.Update -= FrameworkUpdate;

        RestoreYesAlready();
    }

    private void ProcessCommand(string command, string arguments) => _configWindow.Toggle();

    private void SaveYesAlready()
    {
        if (_yesAlreadyState.Saved)
        {
            PluginLog.Information("Not overwriting yesalready state");
            return;
        }

        _yesAlreadyState = (true, _yesAlreadyIpc.DisableIfNecessary());
        PluginLog.Information($"Previous yesalready state: {_yesAlreadyState.PreviousState}");
    }

    private void RestoreYesAlready()
    {
        if (_yesAlreadyState.Saved)
        {
            PluginLog.Information($"Restoring previous yesalready state: {_yesAlreadyState.PreviousState}");
            if (_yesAlreadyState.PreviousState == true)
                _yesAlreadyIpc.Enable();
        }

        _yesAlreadyState = (false, null);
    }
}
