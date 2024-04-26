using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Math;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private unsafe void InteractWithTarget(GameObject obj)
    {
        _pluginLog.Information($"Setting target to {obj}");
        if (_targetManager.Target == null || _targetManager.Target != obj)
        {
            _targetManager.Target = obj;
        }

        TargetSystem.Instance()->InteractWithObject(
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address, false);
    }

    internal unsafe int GetCurrentSealCount()
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
                    return Vector3.Distance(_clientState.LocalPlayer?.Position ?? Vector3.Zero, c.Position);
                }
            }
        }

        o = null;
        return float.MaxValue;
    }

    private static int GetNpcId(GameObject obj)
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

    private uint GetSealCap() => _gcRankInfo.TryGetValue(GetGrandCompanyRank(), out var rank) ? rank.MaxSeals : 0;

    public uint MaxSealCap => _gcRankInfo[11].MaxSeals;

    internal uint GetSealsRequiredForNextRank()
        => _gcRankInfo.GetValueOrDefault(GetGrandCompanyRank())?.RequiredSeals ?? 0;

    internal byte GetRequiredHuntingLogForNextRank()
        => _gcRankInfo.GetValueOrDefault(GetGrandCompanyRank() + 1u)?.RequiredHuntingLog ?? 0;

    internal string? GetNextGrandCompanyRankName()
    {
        bool female = _clientState.LocalPlayer!.Customize[(int)CustomizeIndex.Gender] == 1;
        GrandCompany grandCompany = GetGrandCompany();
        return _gcRankInfo.GetValueOrDefault(GetGrandCompanyRank() + 1u)?.GetName(grandCompany, female);
    }

    public unsafe int GetItemCount(uint itemId, bool checkRetainerInventory)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        int count = inventoryManager->GetInventoryItemCount(itemId, false, false, false);

        if (checkRetainerInventory)
        {
            if (!_retainerItemCache.TryGetValue(itemId, out int retainerCount))
            {
                _retainerItemCache[itemId] = retainerCount = (int)_externalPluginHandler.GetRetainerItemCount(itemId);
            }

            count += retainerCount;
        }

        return count;
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

    /// <summary>
    /// This returns ALL items that can be turned in, regardless of filter settings.
    /// </summary>
    private unsafe List<TurnInItem> BuildTurnInList(AgentGrandCompanySupply* agent)
    {
        List<TurnInItem> list = new();
        for (int i = 11 /* skip over provisioning items */; i < agent->NumItems; ++i)
        {
            GrandCompanyItem item = agent->ItemArray[i];

            // this includes all items, even if they don't match the filter
            list.Add(new TurnInItem
            {
                ItemId = item.ItemId,
                Name = MemoryHelper.ReadSeString(&item.ItemName).ToString(),
                SealsWithBonus = (int)Math.Round(item.SealReward * GetSealMultiplier(), MidpointRounding.AwayFromZero),
                SealsWithoutBonus = item.SealReward,
                ItemUiCategory = item.ItemUiCategory,
            });
        }

        return list.OrderByDescending(x => x.SealsWithBonus)
            .ThenBy(x => x.ItemUiCategory)
            .ThenBy(x => x.ItemId)
            .ToList();
    }
}
