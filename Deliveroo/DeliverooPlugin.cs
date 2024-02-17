using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Deliveroo.External;
using Deliveroo.GameData;
using Deliveroo.Windows;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib;
using LLib.GameUI;
using Lumina.Excel.GeneratedSheets;

namespace Deliveroo;

public sealed partial class DeliverooPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(typeof(DeliverooPlugin).AssemblyQualifiedName);
    private readonly IReadOnlyList<uint> DisabledTurnInItems = new List<uint> { 2820 }.AsReadOnly();

    private readonly DalamudPluginInterface _pluginInterface;
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly ITargetManager _targetManager;
    private readonly ICondition _condition;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _pluginLog;
    private readonly IAddonLifecycle _addonLifecycle;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Configuration _configuration;

    private readonly GameStrings _gameStrings;
    private readonly ExternalPluginHandler _externalPluginHandler;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly GcRewardsCache _gcRewardsCache;

    private readonly IconCache _iconCache;
    private readonly ItemCache _itemCache;
    private readonly ConfigWindow _configWindow;
    private readonly TurnInWindow _turnInWindow;
    private readonly IReadOnlyDictionary<uint, uint> _sealCaps;
    private readonly Dictionary<uint, int> _retainerItemCache = new();

    private Stage _currentStageInternal = Stage.Stopped;
    private DateTime _continueAt = DateTime.MinValue;
    private int _lastTurnInListSize = int.MaxValue;
    private uint _turnInErrors = 0;
    private List<PurchaseItemRequest> _itemsToPurchaseNow = new();

    public DeliverooPlugin(DalamudPluginInterface pluginInterface, IChatGui chatGui, IGameGui gameGui,
        IFramework framework, IClientState clientState, IObjectTable objectTable, ITargetManager targetManager,
        IDataManager dataManager, ICondition condition, ICommandManager commandManager, IPluginLog pluginLog,
        IAddonLifecycle addonLifecycle, ITextureProvider textureProvider)
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
        _pluginLog = pluginLog;
        _addonLifecycle = addonLifecycle;

        _gameStrings = new GameStrings(dataManager, _pluginLog);
        _externalPluginHandler = new ExternalPluginHandler(_pluginInterface, _pluginLog);
        _configuration = (Configuration?)_pluginInterface.GetPluginConfig() ?? new Configuration();
        _gcRewardsCache = new GcRewardsCache(dataManager);
        _iconCache = new IconCache(textureProvider);
        _itemCache = new ItemCache(dataManager);
        _configWindow = new ConfigWindow(_pluginInterface, this, _configuration, _gcRewardsCache, _clientState, _pluginLog, _iconCache);
        _windowSystem.AddWindow(_configWindow);
        _turnInWindow = new TurnInWindow(this, _pluginInterface, _configuration, _condition, _clientState, _gcRewardsCache, _configWindow, _iconCache);
        _windowSystem.AddWindow(_turnInWindow);
        _sealCaps = dataManager.GetExcelSheet<GrandCompanyRank>()!.Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId, x => x.MaxSeals);

        _framework.Update += FrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += _configWindow.Toggle;
        _clientState.Login += Login;
        _clientState.Logout += Logout;
        _clientState.TerritoryChanged += TerritoryChanged;
        _chatGui.ChatMessage += ChatMessage;
        _commandManager.AddHandler("/deliveroo", new CommandInfo(ProcessCommand)
        {
            HelpMessage = "Open the configuration"
        });

        if (_clientState.IsLoggedIn)
            Login();

        if (_configuration.AddVentureIfNoItemToPurchaseSelected())
            _pluginInterface.SavePluginConfig(_configuration);

        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesNoPostSetup);
    }

    private void ChatMessage(XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        if (_configuration.PauseAtRank <= 0)
            return;

        if (type != _gameStrings.RankUpFcType)
            return;

        var match = _gameStrings.RankUpFc.Match(message.ToString());
        if (!match.Success)
            return;

        foreach (var group in match.Groups.Values)
        {
            if (int.TryParse(group.Value, out int rank) && rank == _configuration.PauseAtRank)
            {
                _turnInWindow.State = false;
                _pluginLog.Information($"Pausing GC delivery, FC reached rank {rank}");
                _chatGui.Print($"Pausing Deliveroo, your FC reached rank {rank}.");
                return;
            }
        }
    }

    internal CharacterConfiguration? CharacterConfiguration { get; set; }


    internal Stage CurrentStage
    {
        get => _currentStageInternal;
        set
        {
            if (_currentStageInternal != value)
            {
                _pluginLog.Verbose($"Changing stage from {_currentStageInternal} to {value}");
                _currentStageInternal = value;
            }
        }
    }

    public int EffectiveReservedSealCount
    {
        get
        {
            if (CharacterConfiguration is { IgnoreMinimumSealsToKeep: true })
                return 0;

            return _configuration.ReserveDifferentSealCountAtMaxRank && GetSealCap() == GetMaxSealCap()
                ? _configuration.ReservedSealCountAtMaxRank
                : _configuration.ReservedSealCount;
        }
    }

    private void Login()
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

                _pluginLog.Information($"Loaded character-specific information for {_clientState.LocalContentId}");
            }
            else
            {
                _pluginLog.Verbose(
                    $"No character-specific information for {_clientState.LocalContentId}");
            }
        }
        catch (Exception ex)
        {
            _pluginLog.Error(ex, "Unable to load character configuration");
            CharacterConfiguration = null;
        }
    }

    private void Logout()
    {
        CharacterConfiguration = null;
        _retainerItemCache.Clear();
    }

    private void TerritoryChanged(ushort territoryType)
    {
        // there is no GC area that is in the same zone as a retainer bell, so this should be often enough.
        _retainerItemCache.Clear();
    }

    private unsafe void FrameworkUpdate(IFramework f)
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
                _externalPluginHandler.Restore();
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
                    _externalPluginHandler.Restore();
                    CurrentStage = Stage.Stopped;
                }

                return;
            }
            else if (_turnInWindow.State && CurrentStage == Stage.Stopped)
            {
                CurrentStage = Stage.TargetPersonnelOfficer;
                _itemsToPurchaseNow = _turnInWindow.SelectedItems;
                ResetTurnInErrorHandling();
                if (_itemsToPurchaseNow.Count > 0)
                {
                    _pluginLog.Information("Items to purchase:");
                    foreach (var item in _itemsToPurchaseNow)
                        _pluginLog.Information($"  {item.Name} (limit = {item.EffectiveLimit})");
                }
                else
                    _pluginLog.Information("No items to purchase configured or available");


                var nextItem = GetNextItemToPurchase();
                if (nextItem != null && GetCurrentSealCount() >= EffectiveReservedSealCount + nextItem.SealCost)
                    CurrentStage = Stage.TargetQuartermaster;

                if (_gameGui.TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList", out var gcSupplyList) &&
                    LAddon.IsAddonReady(&gcSupplyList->AtkUnitBase))
                    CurrentStage = Stage.SelectExpertDeliveryTab;

                if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var gcExchange) &&
                    LAddon.IsAddonReady(gcExchange))
                    CurrentStage = Stage.SelectRewardTier;
            }

            if (CurrentStage != Stage.Stopped && CurrentStage != Stage.RequestStop && !_externalPluginHandler.Saved)
                _externalPluginHandler.Save();

            switch (CurrentStage)
            {
                case Stage.TargetPersonnelOfficer:
                    InteractWithPersonnelOfficer(personnelOfficer!, quartermaster!);
                    break;

                case Stage.OpenGcSupply:
                    // see SelectStringPostSetup
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

                case Stage.CloseGcSupplySelectString:
                    // see SelectStringPostSetup
                    break;

                case Stage.CloseGcSupplySelectStringThenStop:
                    // see SelectStringPostSetup
                    break;

                case Stage.CloseGcSupplyWindowThenStop:
                    CloseGcSupplyWindow();
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
                    // see SelectYesNoPostSetup
                    break;

                case Stage.CloseGcExchange:
                    CloseGcExchange();
                    break;

                case Stage.RequestStop:
                    _externalPluginHandler.Restore();
                    CurrentStage = Stage.Stopped;

                    break;
                case Stage.Stopped:
                    break;

                default:
                    _pluginLog.Warning($"Unknown stage {CurrentStage}");
                    break;
            }
        }
    }


    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesNoPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);

        _commandManager.RemoveHandler("/deliveroo");
        _chatGui.ChatMessage -= ChatMessage;
        _clientState.TerritoryChanged -= TerritoryChanged;
        _clientState.Logout -= Logout;
        _clientState.Login -= Login;
        _pluginInterface.UiBuilder.OpenConfigUi -= _configWindow.Toggle;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _framework.Update -= FrameworkUpdate;

        _externalPluginHandler.Restore();

        _externalPluginHandler.Dispose();
        _iconCache.Dispose();
    }

    private void ProcessCommand(string command, string arguments)
    {
        switch (arguments)
        {
            case "e" or "enable":
                if (_turnInWindow.IsOpen)
                    _turnInWindow.State = true;
                break;

            case "d" or "disable":
                _turnInWindow.State = false;
                break;

            default:
                _configWindow.Toggle();
                break;
        }
    }
}
