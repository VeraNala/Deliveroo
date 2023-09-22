using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Gui;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Memory;
using Dalamud.Plugin;
using Deliveroo.External;
using Deliveroo.GameData;
using Deliveroo.Windows;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Deliveroo;

public sealed class DeliverooPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(typeof(DeliverooPlugin).AssemblyQualifiedName);

    private readonly DalamudPluginInterface _pluginInterface;
    private readonly ChatGui _chatGui;
    private readonly GameGui _gameGui;
    private readonly Framework _framework;
    private readonly ClientState _clientState;
    private readonly ObjectTable _objectTable;
    private readonly TargetManager _targetManager;

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
    private GcRewardItem _selectedRewardItem = GcRewardItem.None;
    private (bool Saved, bool? PreviousState) _yesAlreadyState = (false, null);

    public DeliverooPlugin(DalamudPluginInterface pluginInterface, ChatGui chatGui, GameGui gameGui,
        Framework framework, ClientState clientState, ObjectTable objectTable, TargetManager targetManager,
        DataManager dataManager)
    {
        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _targetManager = targetManager;

        var dalamudReflector = new DalamudReflector(_pluginInterface, _framework);
        _yesAlreadyIpc = new YesAlreadyIpc(dalamudReflector);
        _configuration = (Configuration?)_pluginInterface.GetPluginConfig() ?? new Configuration();
        _gcRewardsCache = new GcRewardsCache(dataManager);
        _configWindow = new ConfigWindow(_pluginInterface, this, _configuration, _gcRewardsCache);
        _windowSystem.AddWindow(_configWindow);
        _turnInWindow = new TurnInWindow(this, _pluginInterface, _configuration, _gcRewardsCache);
        _windowSystem.AddWindow(_turnInWindow);
        _sealCaps = dataManager.GetExcelSheet<GrandCompanyRank>()!.Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId, x => x.MaxSeals);

        _framework.Update += FrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        _pluginInterface.UiBuilder.OpenConfigUi += _configWindow.Toggle;
    }

    public string Name => "Deliveroo";

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

    private unsafe void FrameworkUpdate(Framework f)
    {
        if (!_clientState.IsLoggedIn || _clientState.TerritoryType is not 128 and not 130 and not 132 ||
            GetDistanceToNpc(GetQuartermasterId(), out GameObject? quartermaster) >= 7f ||
            GetDistanceToNpc(GetPersonnelOfficerId(), out GameObject? personnelOfficer) >= 7f ||
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
                _selectedRewardItem = _turnInWindow.SelectedItem;
                if (_selectedRewardItem.IsValid() && _selectedRewardItem.RequiredRank > GetGrandCompanyRank())
                    _selectedRewardItem = GcRewardItem.None;

                if (_selectedRewardItem.IsValid() && GetCurrentSealCount() > GetSealCap() / 2)
                    CurrentStage = Stage.TargetQuartermaster;

                if (TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList", out var gcSupplyList) &&
                    IsAddonReady(&gcSupplyList->AtkUnitBase))
                    CurrentStage = Stage.SelectItemToTurnIn;

                if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var gcExchange) &&
                    IsAddonReady(gcExchange))
                    CurrentStage = Stage.CloseGcExchange;
            }

            if (CurrentStage != Stage.Stopped && CurrentStage != Stage.RequestStop && !_yesAlreadyState.Saved)
                SaveYesAlready();

            switch (CurrentStage)
            {
                case Stage.TargetPersonnelOfficer:
                    if (_targetManager.Target == quartermaster!)
                        break;

                    InteractWithTarget(personnelOfficer!);
                    CurrentStage = Stage.OpenGcSupply;
                    break;
                case Stage.OpenGcSupply:
                    if (SelectSelectString(0))
                        CurrentStage = Stage.SelectItemToTurnIn;

                    break;
                case Stage.SelectItemToTurnIn:
                    var agentInterface = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanySupply);
                    if (agentInterface != null && agentInterface->IsAgentActive())
                    {
                        var addonId = agentInterface->GetAddonID();
                        if (addonId == 0)
                            break;

                        AtkUnitBase* addon = GetAddonById(addonId);
                        if (addon == null || !IsAddonReady(addon) || addon->UldManager.NodeListCount <= 20 ||
                            !addon->UldManager.NodeList[5]->IsVisible)
                            break;

                        var addonGc = (AddonGrandCompanySupplyList*)addon;
                        if (addonGc->SelectedTab != 2 || addonGc->SelectedFilter != 1)
                            break;

                        var agent = (AgentGrandCompanySupply*)agentInterface;
                        List<TurnInItem> items = BuildTurnInList(agent);
                        if (items.Count == 0 || addon->UldManager.NodeList[20]->IsVisible)
                        {
                            CurrentStage = Stage.CloseGcSupplyThenStop;
                            addon->FireCallbackInt(-1);
                            break;
                        }

                        if (GetCurrentSealCount() + items[0].SealsWithBonus > GetSealCap())
                        {
                            CurrentStage = Stage.CloseGcSupply;
                            addon->FireCallbackInt(-1);
                            break;
                        }

                        var selectFirstItem = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 1 },
                            new() { Type = ValueType.Int, Int = 0 /* position within list */ },
                            new() { Type = 0, Int = 0 }
                        };
                        addon->FireCallback(3, selectFirstItem);
                        CurrentStage = Stage.TurnInSelected;
                    }

                    break;
                case Stage.TurnInSelected:
                    if (TryGetAddonByName<AddonSelectYesno>("SelectYesno", out var addonSelectYesno) &&
                        IsAddonReady(&addonSelectYesno->AtkUnitBase))
                    {
                        if (MemoryHelper.ReadSeString(&addonSelectYesno->PromptText->NodeText).ToString()
                            .StartsWith("Do you really want to trade a high-quality item?"))
                        {
                            addonSelectYesno->AtkUnitBase.FireCallbackInt(0);
                            break;
                        }
                    }

                    if (TryGetAddonByName<AddonGrandCompanySupplyReward>("GrandCompanySupplyReward",
                            out var addonSupplyReward) && IsAddonReady(&addonSupplyReward->AtkUnitBase))
                    {
                        addonSupplyReward->AtkUnitBase.FireCallbackInt(0);
                        _continueAt = DateTime.Now.AddSeconds(0.58);
                        CurrentStage = Stage.FinalizeTurnIn;
                    }

                    break;

                case Stage.FinalizeTurnIn:
                    if (TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList",
                            out var addonSupplyList) && IsAddonReady(&addonSupplyList->AtkUnitBase))
                    {
                        var updateFilter = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 5 },
                            new() { Type = ValueType.Int, Int = addonSupplyList->SelectedFilter },
                            new() { Type = 0, Int = 0 }
                        };
                        addonSupplyList->AtkUnitBase.FireCallback(3, updateFilter);
                        CurrentStage = Stage.SelectItemToTurnIn;
                    }

                    break;

                case Stage.CloseGcSupply:
                    if (SelectSelectString(3))
                    {
                        if (!_selectedRewardItem.IsValid())
                        {
                            _turnInWindow.State = false;
                            CurrentStage = Stage.RequestStop;
                        }
                        else
                        {
                            // you can occasionally get a 'not enough seals' warning lol
                            _continueAt = DateTime.Now.AddSeconds(1);
                            CurrentStage = Stage.TargetQuartermaster;
                        }
                    }

                    break;

                case Stage.CloseGcSupplyThenStop:
                    if (SelectSelectString(3))
                    {
                        if (!_selectedRewardItem.IsValid())
                        {
                            _turnInWindow.State = false;
                            CurrentStage = Stage.RequestStop;
                        }
                        else if (GetCurrentSealCount() <=
                                 _configuration.ReservedSealCount + _selectedRewardItem.SealCost)
                        {
                            _turnInWindow.State = false;
                            CurrentStage = Stage.RequestStop;
                        }
                        else
                        {
                            _continueAt = DateTime.Now.AddSeconds(1);
                            CurrentStage = Stage.TargetQuartermaster;
                        }
                    }

                    break;

                case Stage.TargetQuartermaster:
                    if (GetCurrentSealCount() < _configuration.ReservedSealCount)
                    {
                        CurrentStage = Stage.RequestStop;
                        break;
                    }

                    if (_targetManager.Target == personnelOfficer!)
                        break;

                    InteractWithTarget(quartermaster!);
                    CurrentStage = Stage.SelectRewardTier;
                    break;

                case Stage.SelectRewardTier:
                {
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        PluginLog.Information($"Selecting tier 1, {(int)_selectedRewardItem.Tier - 1}");
                        var selectRank = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 1 },
                            new() { Type = ValueType.Int, Int = (int)_selectedRewardItem.Tier - 1 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 }
                        };
                        addonExchange->FireCallback(9, selectRank);
                        _continueAt = DateTime.Now.AddSeconds(0.5);
                        CurrentStage = Stage.SelectRewardSubCategory;
                    }

                    break;
                }

                case Stage.SelectRewardSubCategory:
                {
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        PluginLog.Information($"Selecting subcategory 2, {(int)_selectedRewardItem.SubCategory}");
                        var selectType = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 2 },
                            new() { Type = ValueType.Int, Int = (int)_selectedRewardItem.SubCategory },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 }
                        };
                        addonExchange->FireCallback(9, selectType);
                        _continueAt = DateTime.Now.AddSeconds(0.5);
                        CurrentStage = Stage.SelectReward;
                    }

                    break;
                }

                case Stage.SelectReward:
                {
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        if (SelectRewardItem(addonExchange))
                        {
                            _continueAt = DateTime.Now.AddSeconds(0.5);
                            CurrentStage = Stage.ConfirmReward;
                        }
                        else
                        {
                            PluginLog.Warning("Could not find selected reward item");
                            _continueAt = DateTime.Now.AddSeconds(0.5);
                            CurrentStage = Stage.CloseGcExchange;
                        }
                    }

                    break;
                }

                case Stage.ConfirmReward:
                    if (SelectSelectYesno(0))
                    {
                        CurrentStage = Stage.CloseGcExchange;
                        _continueAt = DateTime.Now.AddSeconds(0.5);
                    }

                    break;

                case Stage.CloseGcExchange:
                {
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        addonExchange->FireCallbackInt(-1);
                        CurrentStage = Stage.TargetPersonnelOfficer;
                    }

                    break;
                }

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

    private float GetDistanceToNpc(int npcId, out GameObject? o)
    {
        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind == ObjectKind.EventNpc && obj is Character c)
            {
                if (GetNpcId(obj) == npcId)
                {
                    o = obj;
                    return Vector3.Distance(_clientState.LocalPlayer!.Position, c.Position);
                }
            }
        }

        o = null;
        return float.MaxValue;
    }

    private int GetNpcId(GameObject obj)
    {
        return Marshal.ReadInt32(obj.Address + 128);
    }

    public void Dispose()
    {
        _pluginInterface.UiBuilder.OpenConfigUi -= _configWindow.Toggle;
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _framework.Update -= FrameworkUpdate;

        RestoreYesAlready();
    }

    private unsafe List<TurnInItem> BuildTurnInList(AgentGrandCompanySupply* agent)
    {
        List<TurnInItem> list = new();
        for (int i = 11 /* skip over provisioning items */; i < agent->NumItems; ++i)
        {
            GrandCompanyItem item = agent->ItemArray[i];

            // this includes all items, even if they don't match the filter
            list.Add(new TurnInItem
            {
                ItemId = Marshal.ReadInt32(new nint(&item) + 132),
                Name = MemoryHelper.ReadSeString(&item.ItemName).ToString(),
                SealsWithBonus = (int)Math.Round(item.SealReward * GetSealMultiplier(), MidpointRounding.AwayFromZero),
                SealsWithoutBonus = item.SealReward,
                ItemUiCategory = Marshal.ReadByte(new nint(&item) + 150),
            });

            // GrandCompanyItem + 104 = [int] InventoryType
            // GrandCompanyItem + 108 = [int] ??
            // GrandCompanyItem + 124 = [int] <Item's Column 19 in the sheet, but that has no name>
            // GrandCompanyItem + 132 = [int] itemId
            // GrandCompanyItem + 136 = [int] 0 (always)?
            // GrandCompanyItem + 140 = [int] i (item's own position within the unsorted list)
            // GrandCompanyItem + 148 = [short] ilvl
            // GrandCompanyItem + 150 = [byte] ItemUICategory
            // GrandCompanyItem + 151 = [byte] (unchecked) inventory slot in container
            // GrandCompanyItem + 152 = [short] 512 (always)?
            // int itemId = Marshal.ReadInt32(new nint(&item) + 132);
            // PluginLog.Verbose($" {Marshal.ReadInt32(new nint(&item) + 132)};;;; {MemoryHelper.ReadSeString(&item.ItemName)}, {new nint(&agent->ItemArray[i]):X8}, {item.SealReward}, {item.IsTurnInAvailable}");
        }

        return list.OrderByDescending(x => x.SealsWithBonus)
            .ThenBy(x => x.ItemUiCategory)
            .ThenBy(x => x.ItemId)
            .ToList();
    }

    private unsafe AtkUnitBase* GetAddonById(uint id)
    {
        var unitManagers = &AtkStage.GetSingleton()->RaptureAtkUnitManager->AtkUnitManager.DepthLayerOneList;
        for (var i = 0; i < 18; i++)
        {
            var unitManager = &unitManagers[i];
            var unitBaseArray = &(unitManager->AtkUnitEntries);
            for (var j = 0; j < unitManager->Count; j++)
            {
                var unitBase = unitBaseArray[j];
                if (unitBase->ID == id)
                {
                    return unitBase;
                }
            }
        }

        return null;
    }

    private unsafe bool TryGetAddonByName<T>(string addonName, out T* addonPtr)
        where T : unmanaged
    {
        var a = _gameGui.GetAddonByName(addonName);
        if (a != IntPtr.Zero)
        {
            addonPtr = (T*)a;
            return true;
        }
        else
        {
            addonPtr = null;
            return false;
        }
    }

    private unsafe bool IsAddonReady(AtkUnitBase* addon)
    {
        return addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded;
    }

    private unsafe bool SelectRewardItem(AtkUnitBase* addonExchange)
    {
        uint itemsOnCurrentPage = addonExchange->AtkValues[1].UInt;
        for (uint i = 0; i < itemsOnCurrentPage; ++i)
        {
            uint itemId = addonExchange->AtkValues[317 + i].UInt;
            if (itemId == _selectedRewardItem.ItemId)
            {
                long toBuy = (GetCurrentSealCount() - _configuration.ReservedSealCount) / _selectedRewardItem.SealCost;
                bool isVenture = _selectedRewardItem.ItemId == ItemIds.Venture;
                if (isVenture)
                    toBuy = Math.Min(toBuy, 65000 - GetCurrentVentureCount());

                if (toBuy == 0)
                {
                    _turnInWindow.State = false;
                    CurrentStage = Stage.RequestStop;
                    break;
                }

                PluginLog.Information($"Selecting item {itemId}, {i}");
                _chatGui.Print($"Buying {toBuy}x {_selectedRewardItem.Name}...");
                var selectReward = stackalloc AtkValue[]
                {
                    new() { Type = ValueType.Int, Int = 0 },
                    new() { Type = ValueType.Int, Int = (int)i },
                    new() { Type = ValueType.Int, Int = (int)toBuy },
                    new() { Type = 0, Int = 0 },
                    new() { Type = ValueType.Bool, Byte = 1 },
                    new() { Type = ValueType.Bool, Byte = 0 },
                    new() { Type = 0, Int = 0 },
                    new() { Type = 0, Int = 0 },
                    new() { Type = 0, Int = 0 }
                };
                addonExchange->FireCallback(9, selectReward);
                return true;
            }
        }

        return false;
    }

    private unsafe int GetCurrentSealCount()
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        switch ((GrandCompany)PlayerState.Instance()->GrandCompany)
        {
            case GrandCompany.Maelstrom:
                return inventoryManager->GetInventoryItemCount(20, false, false, false);
            case GrandCompany.TwinAdder:
                return inventoryManager->GetInventoryItemCount(21, false, false, false);
            case GrandCompany.ImmortalFlames:
                return inventoryManager->GetInventoryItemCount(22, false, false, false);
            default:
                return 0;
        }
    }

    internal unsafe GrandCompany GetGrandCompany() => (GrandCompany)PlayerState.Instance()->GrandCompany;

    internal unsafe byte GetGrandCompanyRank() => PlayerState.Instance()->GetGrandCompanyRank();

    private int GetPersonnelOfficerId()
    {
        return GetGrandCompany() switch
        {
            GrandCompany.Maelstrom => 0xF4B94,
            GrandCompany.ImmortalFlames => 0xF4B97,
            GrandCompany.TwinAdder => 0xF4B9A,
            _ => int.MaxValue,
        };
    }

    private int GetQuartermasterId()
    {
        return GetGrandCompany() switch
        {
            GrandCompany.Maelstrom => 0xF4B93,
            GrandCompany.ImmortalFlames => 0xF4B96,
            GrandCompany.TwinAdder => 0xF4B99,
            _ => int.MaxValue,
        };
    }

    private uint GetSealCap() => _sealCaps.TryGetValue(GetGrandCompanyRank(), out var cap) ? cap : 0;

    private unsafe int GetCurrentVentureCount()
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager->GetInventoryItemCount(ItemIds.Venture, false, false, false);
    }

    private unsafe bool SelectSelectString(int choice)
    {
        if (TryGetAddonByName<AddonSelectString>("SelectString", out var addonSelectString) &&
            IsAddonReady(&addonSelectString->AtkUnitBase))
        {
            addonSelectString->AtkUnitBase.FireCallbackInt(choice);
            return true;
        }

        return false;
    }

    private unsafe bool SelectSelectYesno(int choice)
    {
        if (TryGetAddonByName<AddonSelectYesno>("SelectYesno", out var addonSelectYesno) &&
            IsAddonReady(&addonSelectYesno->AtkUnitBase))
        {
            PluginLog.Information(
                $"Selecting choice={choice} for '{MemoryHelper.ReadSeString(&addonSelectYesno->PromptText->NodeText)}'");

            addonSelectYesno->AtkUnitBase.FireCallbackInt(choice);
            return true;
        }

        return false;
    }

    private decimal GetSealMultiplier()
    {
        // priority seal allowance
        if (_clientState.LocalPlayer!.StatusList.Any(x => x.StatusId == 1078))
            return 1.15m;

        // seal sweetener 1/2
        var fcStatus = _clientState.LocalPlayer!.StatusList.FirstOrDefault(x => x.StatusId == 414);
        if (fcStatus != null)
        {
            return 1m + fcStatus.StackCount / 100m;
        }

        return 1;
    }

    private unsafe void InteractWithTarget(GameObject obj)
    {
        PluginLog.Information($"Setting target to {obj}");
        if (_targetManager.Target == null || _targetManager.Target != obj)
        {
            _targetManager.Target = obj;
        }

        TargetSystem.Instance()->InteractWithObject(
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address, false);
    }

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

    internal enum Stage
    {
        TargetPersonnelOfficer,
        OpenGcSupply,
        SelectItemToTurnIn,
        TurnInSelected,
        FinalizeTurnIn,
        CloseGcSupply,
        CloseGcSupplyThenStop,

        TargetQuartermaster,
        SelectRewardTier,
        SelectRewardSubCategory,
        SelectReward,
        ConfirmReward,
        CloseGcExchange,

        RequestStop,
        Stopped,
    }
}
