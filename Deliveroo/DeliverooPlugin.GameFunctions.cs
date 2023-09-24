using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Dalamud.Memory;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Deliveroo;

partial class DeliverooPlugin
{
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

    public unsafe int GetItemCount(uint itemId)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager->GetInventoryItemCount(itemId, false, false, false);
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

    private unsafe bool SelectSelectYesno(int choice, Predicate<string> predicate)
    {
        if (TryGetAddonByName<AddonSelectYesno>("SelectYesno", out var addonSelectYesno) &&
            IsAddonReady(&addonSelectYesno->AtkUnitBase) &&
            predicate(MemoryHelper.ReadSeString(&addonSelectYesno->PromptText->NodeText).ToString()))
        {
            PluginLog.Information(
                $"Selecting choice={choice} for '{MemoryHelper.ReadSeString(&addonSelectYesno->PromptText->NodeText)}'");

            addonSelectYesno->AtkUnitBase.FireCallbackInt(choice);
            return true;
        }

        return false;
    }
}
