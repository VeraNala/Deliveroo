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
using Deliveroo.Handlers;
using Deliveroo.Windows;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib;
using LLib.GameUI;

namespace Deliveroo;

public sealed partial class DeliverooPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(typeof(DeliverooPlugin).AssemblyQualifiedName);

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    private readonly IFramework _framework;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly ICommandManager _commandManager;
    private readonly IPluginLog _pluginLog;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IKeyState _keyState;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly Configuration _configuration;

    private readonly GameStrings _gameStrings;
    private readonly GameFunctions _gameFunctions;
    private readonly ExternalPluginHandler _externalPluginHandler;

    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly GcRewardsCache _gcRewardsCache;

    private readonly IconCache _iconCache;
    private readonly ItemCache _itemCache;
    private readonly ExchangeHandler _exchangeHandler;
    private readonly SupplyHandler _supplyHandler;
    private readonly ConfigWindow _configWindow;
    private readonly TurnInWindow _turnInWindow;

    private Stage _currentStageInternal = Stage.Stopped;

    public DeliverooPlugin(IDalamudPluginInterface pluginInterface, IChatGui chatGui, IGameGui gameGui,
        IFramework framework, IClientState clientState, IObjectTable objectTable, ITargetManager targetManager,
        IDataManager dataManager, ICondition condition, ICommandManager commandManager, IPluginLog pluginLog,
        IAddonLifecycle addonLifecycle, ITextureProvider textureProvider, IGameConfig gameConfig, IKeyState keyState)
    {
        ArgumentNullException.ThrowIfNull(dataManager);

        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _framework = framework;
        _clientState = clientState;
        _condition = condition;
        _commandManager = commandManager;
        _pluginLog = pluginLog;
        _addonLifecycle = addonLifecycle;
        _keyState = keyState;

        _configuration = (Configuration?)_pluginInterface.GetPluginConfig() ?? new Configuration();
        MigrateConfiguration();

        _gameStrings = new GameStrings(dataManager, _pluginLog);
        _externalPluginHandler = new ExternalPluginHandler(_pluginInterface, gameConfig, _configuration, _pluginLog);
        _gameFunctions = new GameFunctions(objectTable, _clientState, targetManager, dataManager,
            _externalPluginHandler, _pluginLog);
        _gcRewardsCache = new GcRewardsCache(dataManager);
        _iconCache = new IconCache(textureProvider);
        _itemCache = new ItemCache(dataManager);

        _exchangeHandler = new ExchangeHandler(this, _gameFunctions, targetManager, _gameGui, _chatGui, _pluginLog);
        _supplyHandler = new SupplyHandler(this, _gameFunctions, targetManager, _gameGui, _pluginLog);

        _configWindow = new ConfigWindow(_pluginInterface, this, _configuration, _gcRewardsCache, _clientState,
            _pluginLog, _iconCache, _gameFunctions);
        _windowSystem.AddWindow(_configWindow);
        _turnInWindow = new TurnInWindow(this, _pluginInterface, _configuration, _condition, _clientState,
            _gcRewardsCache, _configWindow, _iconCache, _keyState, _gameFunctions);
        _windowSystem.AddWindow(_turnInWindow);

        _framework.Update += FrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += _configWindow.Toggle;
        _clientState.Login += Login;
        _clientState.Logout += Logout;
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
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "GrandCompanySupplyReward", GrandCompanySupplyRewardPostSetup);
    }

    private void MigrateConfiguration()
    {
#pragma warning disable CS0612 // Type or member is obsolete
        if (_configuration.Version == 1)
        {
            _configuration.ItemsAvailableToPurchase = _configuration.ItemsAvailableForPurchase.Select(x =>
                new Configuration.PurchaseOption
                {
                    ItemId = x,
                    SameQuantityForAllLists = false,
                }).ToList();
            _configuration.Version = 2;
            _pluginInterface.SavePluginConfig(_configuration);
        }
#pragma warning restore CS0612 // Type or member is obsolete
    }

    private void ChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message,
        ref bool isHandled)
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
                DeliveryResult = new MessageDeliveryResult
                {
                    Message = new SeStringBuilder()
                        .Append($"Pausing Deliveroo, your FC reached rank {rank}.")
                        .Build(),
                };
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

    internal DateTime ContinueAt { private get; set; } = DateTime.MinValue;
    internal List<PurchaseItemRequest> ItemsToPurchaseNow { get; private set; } = new();
    internal int LastTurnInListSize { get; set; } = int.MaxValue;
    internal IDeliveryResult? DeliveryResult { get; set; }

    internal bool TurnInState
    {
        set => _turnInWindow.State = value;
    }

    internal string TurnInError
    {
        set => _turnInWindow.Error = value;
    }

    public int EffectiveReservedSealCount
    {
        get
        {
            if (CharacterConfiguration is { IgnoreMinimumSealsToKeep: true })
                return 0;

            return _configuration.ReserveDifferentSealCountAtMaxRank &&
                   _gameFunctions.GetSealCap() == _gameFunctions.MaxSealCap
                ? _configuration.ReservedSealCountAtMaxRank
                : _configuration.ReservedSealCount;
        }
    }

    private void Login()
    {
        _framework.RunOnFrameworkThread(() =>
        {
            try
            {
                CharacterConfiguration = CharacterConfiguration.Load(_pluginInterface, _clientState.LocalContentId);
                if (CharacterConfiguration != null)
                {
                    if (CharacterConfiguration.CachedPlayerName != _clientState.LocalPlayer!.Name.ToString() ||
                        CharacterConfiguration.CachedWorldName !=
                        _clientState.LocalPlayer.HomeWorld.Value.Name.ToString())
                    {
                        CharacterConfiguration.CachedPlayerName = _clientState.LocalPlayer!.Name.ToString();
                        CharacterConfiguration.CachedWorldName =
                            _clientState.LocalPlayer.HomeWorld.Value.Name.ToString();

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
        });
    }

    private void Logout(int type, int code)
    {
        CharacterConfiguration = null;
    }

    private unsafe void FrameworkUpdate(IFramework f)
    {
        _turnInWindow.Error = string.Empty;
        if (!_clientState.IsLoggedIn ||
            _clientState.TerritoryType is not 128 and not 130 and not 132 ||
            _condition[ConditionFlag.OccupiedInCutSceneEvent] ||
            _gameFunctions.GetDistanceToNpc(_gameFunctions.GetQuartermasterId(), out IGameObject? quartermaster) >= 7f ||
            _gameFunctions.GetDistanceToNpc(_gameFunctions.GetPersonnelOfficerId(), out IGameObject? personnelOfficer) >=
            7f ||
            CharacterConfiguration is { DisableForCharacter: true } ||
            _configWindow.IsOpen)
        {
            _turnInWindow.IsOpen = false;
            _turnInWindow.State = false;
            StopTurnIn();
        }
        else if (DateTime.Now > ContinueAt)
        {
            _turnInWindow.IsOpen = true;
            _turnInWindow.Multiplier = _gameFunctions.GetSealMultiplier();

            if (!_turnInWindow.State)
            {
                StopTurnIn();
                return;
            }
            else if (_turnInWindow.State && CurrentStage == Stage.Stopped)
            {
                CurrentStage = Stage.TargetPersonnelOfficer;
                ItemsToPurchaseNow = _turnInWindow.SelectedItems;
                DeliveryResult = new MessageDeliveryResult();
                _supplyHandler.ResetTurnInErrorHandling();
                if (ItemsToPurchaseNow.Count > 0)
                {
                    _pluginLog.Information("Items to purchase:");
                    foreach (var item in ItemsToPurchaseNow)
                        _pluginLog.Information($"  {item.Name} (limit = {item.EffectiveLimit})");
                }
                else
                    _pluginLog.Information("No items to purchase configured or available");


                var nextItem = _exchangeHandler.GetNextItemToPurchase();
                if (nextItem != null && _gameFunctions.GetCurrentSealCount() >=
                    EffectiveReservedSealCount + nextItem.SealCost)
                    CurrentStage = Stage.TargetQuartermaster;

                if (_gameGui.TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList",
                        out var gcSupplyList) &&
                    LAddon.IsAddonReady(&gcSupplyList->AtkUnitBase))
                    CurrentStage = Stage.SelectExpertDeliveryTab;

                if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var gcExchange) &&
                    LAddon.IsAddonReady(gcExchange))
                    CurrentStage = Stage.SelectRewardTier;

                if (_gameGui.TryGetAddonByName<AddonSelectString>("SelectString", out var addonSelectString) &&
                    LAddon.IsAddonReady(&addonSelectString->AtkUnitBase))
                {
                    if (SelectStringPostSetup(addonSelectString, Stage.OpenGcSupply))
                        return;
                }
            }

            if (CurrentStage != Stage.Stopped && CurrentStage != Stage.RequestStop && !_externalPluginHandler.Saved)
                _externalPluginHandler.Save();

            switch (CurrentStage)
            {
                case Stage.TargetPersonnelOfficer:
                    _supplyHandler.InteractWithPersonnelOfficer(personnelOfficer!, quartermaster!);
                    break;

                case Stage.OpenGcSupply:
                    // see SelectStringPostSetup
                    break;

                case Stage.SelectExpertDeliveryTab:
                    _supplyHandler.SelectExpertDeliveryTab();
                    break;

                case Stage.SelectItemToTurnIn:
                    _supplyHandler.SelectItemToTurnIn();
                    break;

                case Stage.TurnInSelected:
                    // see GrandCompanySupplyReward
                    break;

                case Stage.FinalizeTurnIn:
                case Stage.SingleFinalizeTurnIn:
                    _supplyHandler.FinalizeTurnInItem();
                    break;

                case Stage.CloseGcSupplySelectString:
                    // see SelectStringPostSetup
                    break;

                case Stage.CloseGcSupplySelectStringThenStop:
                    // see SelectStringPostSetup
                    break;

                case Stage.CloseGcSupplyWindowThenStop:
                    _supplyHandler.CloseGcSupplyWindow();
                    break;

                case Stage.TargetQuartermaster:
                    _exchangeHandler.InteractWithQuartermaster(personnelOfficer!, quartermaster!);
                    break;

                case Stage.SelectRewardTier:
                    _exchangeHandler.SelectRewardTier();
                    break;

                case Stage.SelectRewardSubCategory:
                    _exchangeHandler.SelectRewardSubCategory();
                    break;

                case Stage.SelectReward:
                    _exchangeHandler.SelectReward();
                    break;

                case Stage.ConfirmReward:
                    // see SelectYesNoPostSetup
                    break;

                case Stage.CloseGcExchange:
                    _exchangeHandler.CloseGcExchange();
                    break;

                case Stage.RequestStop:
                    StopTurnIn();
                    break;

                case Stage.Stopped:
                    break;

                default:
                    _pluginLog.Warning($"Unknown stage {CurrentStage}");
                    break;
            }
        }
    }

    private void StopTurnIn()
    {
        if (CurrentStage != Stage.Stopped)
        {
            _externalPluginHandler.Restore();
            CurrentStage = Stage.Stopped;

            if (DeliveryResult is null or MessageDeliveryResult)
            {
                var text = (DeliveryResult as MessageDeliveryResult)?.Message ?? "Delivery completed.";
                var message = _configuration.ChatType switch
                {
                    XivChatType.Say
                        or XivChatType.Shout
                        or XivChatType.TellOutgoing
                        or XivChatType.TellIncoming
                        or XivChatType.Party
                        or XivChatType.Alliance
                        or (>= XivChatType.Ls1 and <= XivChatType.Ls8)
                        or XivChatType.FreeCompany
                        or XivChatType.NoviceNetwork
                        or XivChatType.Yell
                        or XivChatType.CrossParty
                        or XivChatType.PvPTeam
                        or XivChatType.CrossLinkShell1
                        or XivChatType.NPCDialogue
                        or XivChatType.NPCDialogueAnnouncements
                        or (>= XivChatType.CrossLinkShell2 and <= XivChatType.CrossLinkShell8)
                        => new XivChatEntry
                        {
                            Message = text,
                            Type = _configuration.ChatType,
                            Name = new SeStringBuilder().AddUiForeground("Deliveroo", 52).Build(),
                        },
                    _ => new XivChatEntry
                    {
                        Message = new SeStringBuilder().AddUiForeground("[Deliveroo] ", 52)
                            .Append(text)
                            .Build(),
                        Type = _configuration.ChatType,
                    }
                };
                _chatGui.Print(message);
            }

            DeliveryResult = null;
        }
    }

    public void Dispose()
    {
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "GrandCompanySupplyReward", GrandCompanySupplyRewardPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesNoPostSetup);
        _addonLifecycle.UnregisterListener(AddonEvent.PostSetup, "SelectString", SelectStringPostSetup);

        _commandManager.RemoveHandler("/deliveroo");
        _chatGui.ChatMessage -= ChatMessage;
        _clientState.Logout -= Logout;
        _clientState.Login -= Login;
        _pluginInterface.UiBuilder.OpenConfigUi -= _configWindow.Toggle;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _framework.Update -= FrameworkUpdate;

        _gameFunctions.Dispose();
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
