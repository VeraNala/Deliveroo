using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
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
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Deliveroo;

public class DeliverooPlugin : IDalamudPlugin
{
    private readonly WindowSystem _windowSystem = new(typeof(DeliverooPlugin).AssemblyQualifiedName);

    private readonly DalamudPluginInterface _pluginInterface;
    private readonly ChatGui _chatGui;
    private readonly GameGui _gameGui;
    private readonly Framework _framework;
    private readonly ClientState _clientState;
    private readonly ObjectTable _objectTable;
    private readonly TargetManager _targetManager;

    private readonly TurnInWindow _turnInWindow;

    private Stage _currentStageInternal = Stage.Stop;
    private DateTime _continueAt = DateTime.MinValue;

    public DeliverooPlugin(DalamudPluginInterface pluginInterface, ChatGui chatGui, GameGui gameGui,
        Framework framework, ClientState clientState, ObjectTable objectTable, TargetManager targetManager)
    {
        _pluginInterface = pluginInterface;
        _chatGui = chatGui;
        _gameGui = gameGui;
        _framework = framework;
        _clientState = clientState;
        _objectTable = objectTable;
        _targetManager = targetManager;

        _turnInWindow = new TurnInWindow();
        _windowSystem.AddWindow(_turnInWindow);

        _framework.Update += FrameworkUpdate;
        _pluginInterface.UiBuilder.Draw += _windowSystem.Draw;
    }

    public string Name => "Deliveroo";

    private Stage CurrentStage
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
            GetDistanceToNpc(GetPersonnelOfficerId(), out GameObject? personnelOfficer) >= 7f)
        {
            _turnInWindow.IsOpen = false;
        }
        else if (DateTime.Now > _continueAt)
        {
            _turnInWindow.IsOpen = true;
            _turnInWindow.Multiplier = GetSealMultiplier();
            _turnInWindow.CurrentVentureCount = GetCurrentVentureCount();

            if (!_turnInWindow.State)
            {
                CurrentStage = Stage.Stop;
                return;
            }

            if (_turnInWindow.State && CurrentStage == Stage.Stop)
            {
                CurrentStage = Stage.TargetPersonnelOfficer;
            }

            _turnInWindow.Debug = CurrentStage.ToString();
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
                        List<GcItem> items = BuildTurnInList(agent);
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
                        // you can occasionally get a 'not enough seals' warning lol
                        _continueAt = DateTime.Now.AddSeconds(1);
                        CurrentStage = Stage.TargetQuartermaster;
                    }

                    break;

                case Stage.CloseGcSupplyThenStop:
                    if (SelectSelectString(3))
                    {
                        if (GetCurrentSealCount() <= 2000 + 200)
                        {
                            _turnInWindow.State = false;
                            CurrentStage = Stage.Stop;
                        }
                        else
                        {
                            _continueAt = DateTime.Now.AddSeconds(1);
                            CurrentStage = Stage.TargetQuartermaster;
                        }
                    }

                    break;

                case Stage.TargetQuartermaster:
                    if (GetCurrentSealCount() < 2000) // fixme this should be selectable/dependent on shop item
                    {
                        CurrentStage = Stage.Stop;
                        break;
                    }

                    if (_targetManager.Target == personnelOfficer!)
                        break;

                    InteractWithTarget(quartermaster!);
                    CurrentStage = Stage.SelectRewardRank;
                    break;

                case Stage.SelectRewardRank:
                {
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        var selectRank = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 1 },
                            new() { Type = ValueType.Int, Int = 0 /* position within list */ },
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
                        CurrentStage = Stage.SelectRewardType;
                    }

                    break;
                }

                case Stage.SelectRewardType:
                {
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        var selectType = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 2 },
                            /*
                             * 2 = weapons
                             * 3 = armor
                             * 1 = materiel
                             * 4 = materials
                             */
                            new() { Type = ValueType.Int, Int = 1 /* position within list */ },
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
                    // coke: 0i, 31i, 5i[count], unknown, true, false, unknown, unknown, unknown
                    if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
                        IsAddonReady(addonExchange))
                    {
                        int venturesToBuy = (GetCurrentSealCount() - 2000) / 200;
                        venturesToBuy = Math.Min(venturesToBuy, 65000 - GetCurrentVentureCount());
                        if (venturesToBuy == 0)
                        {
                            CurrentStage = Stage.Stop;
                            break;
                        }

                        _chatGui.Print($"Buying {venturesToBuy} ventures...");
                        var selectReward = stackalloc AtkValue[]
                        {
                            new() { Type = ValueType.Int, Int = 0 },
                            new() { Type = ValueType.Int, Int = 0 /* position within list?? */ },
                            new() { Type = ValueType.Int, Int = venturesToBuy },
                            new() { Type = 0, Int = 0 },
                            new() { Type = ValueType.Bool, Byte = 1 },
                            new() { Type = ValueType.Bool, Byte = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 },
                            new() { Type = 0, Int = 0 }
                        };
                        addonExchange->FireCallback(9, selectReward);
                        _continueAt = DateTime.Now.AddSeconds(1);
                        CurrentStage = Stage.CloseGcExchange;
                    }

                    break;
                }

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

                case Stage.Stop:
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
        _pluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        _framework.Update -= FrameworkUpdate;
    }

    private unsafe List<GcItem> BuildTurnInList(AgentGrandCompanySupply* agent)
    {
        List<GcItem> list = new();
        for (int i = 11 /* skip over provisioning items */; i < agent->NumItems; ++i)
        {
            GrandCompanyItem item = agent->ItemArray[i];

            // this includes all items, even if they don't match the filter
            list.Add(new GcItem
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
            PluginLog.Verbose(
                $" {Marshal.ReadInt32(new nint(&item) + 132)};;;; {MemoryHelper.ReadSeString(&item.ItemName)}, {new nint(&agent->ItemArray[i]):X8}, {item.SealReward}, {item.IsTurnInAvailable}");
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

    private unsafe int GetPersonnelOfficerId()
    {
        return ((GrandCompany)PlayerState.Instance()->GrandCompany) switch
        {
            GrandCompany.Maelstrom => 0xF4B94,
            GrandCompany.ImmortalFlames => 0xF4B97,
            GrandCompany.TwinAdder => 0xF4B9A,
            _ => int.MaxValue,
        };
    }

    private unsafe int GetQuartermasterId()
    {
        return ((GrandCompany)PlayerState.Instance()->GrandCompany) switch
        {
            GrandCompany.Maelstrom => 0xF4B93,
            GrandCompany.ImmortalFlames => 0xF4B96,
            GrandCompany.TwinAdder => 0xF4B99,
            _ => int.MaxValue,
        };
    }

    private unsafe int GetSealCap()
    {
        return PlayerState.Instance()->GetGrandCompanyRank() switch
        {
            1 => 10_000,
            2 => 15_000,
            3 => 20_000,
            4 => 25_000,
            5 => 30_000,
            6 => 35_000,
            7 => 40_000,
            8 => 45_000,
            9 => 50_000,
            10 => 80_000,
            11 => 90_000,
            _ => 0,
        };
    }

    private unsafe int GetCurrentVentureCount()
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager->GetInventoryItemCount(21072, false, false, false);
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

    private enum Stage
    {
        TargetPersonnelOfficer,
        OpenGcSupply,
        SelectItemToTurnIn,
        TurnInSelected,
        FinalizeTurnIn,
        CloseGcSupply,
        CloseGcSupplyThenStop,

        TargetQuartermaster,
        SelectRewardRank,
        SelectRewardType,
        SelectReward,
        CloseGcExchange,

        Stop,
    }
}
