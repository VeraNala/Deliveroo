using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using Deliveroo.External;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.FFXIV.Component.Excel;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Deliveroo;

internal sealed class GameFunctions : IDisposable
{
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly ITargetManager _targetManager;
    private readonly ExternalPluginHandler _externalPluginHandler;
    private readonly IPluginLog _pluginLog;
    private readonly ReadOnlyDictionary<uint, GcRankInfo> _gcRankInfo;
    private readonly Dictionary<uint, int> _retainerItemCache = new();

    public GameFunctions(IObjectTable objectTable, IClientState clientState, ITargetManager targetManager,
        IDataManager dataManager, ExternalPluginHandler externalPluginHandler, IPluginLog pluginLog)
    {
        _objectTable = objectTable;
        _clientState = clientState;
        _targetManager = targetManager;
        _externalPluginHandler = externalPluginHandler;
        _pluginLog = pluginLog;


        _gcRankInfo = dataManager.GetExcelSheet<GrandCompanyRank>().Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId, x => new GcRankInfo
            {
                NameTwinAddersMale = ExtractRankName<GCRankGridaniaMaleText>(dataManager, x.RowId, r => r.Singular),
                NameTwinAddersFemale = ExtractRankName<GCRankGridaniaFemaleText>(dataManager, x.RowId, r => r.Singular),
                NameMaelstromMale = ExtractRankName<GCRankLimsaMaleText>(dataManager, x.RowId, r => r.Singular),
                NameMaelstromFemale = ExtractRankName<GCRankLimsaFemaleText>(dataManager, x.RowId, r => r.Singular),
                NameImmortalFlamesMale = ExtractRankName<GCRankUldahMaleText>(dataManager, x.RowId, r => r.Singular),
                NameImmortalFlamesFemale =
                    ExtractRankName<GCRankUldahFemaleText>(dataManager, x.RowId, r => r.Singular),
                MaxSeals = x.MaxSeals,
                RequiredSeals = x.RequiredSeals,
                RequiredHuntingLog = x.Unknown0,
            })
            .AsReadOnly();

        _clientState.Logout += Logout;
        _clientState.TerritoryChanged += TerritoryChanged;
    }

    private static string ExtractRankName<T>(IDataManager dataManager, uint rankId, Func<T, ReadOnlySeString> func)
        where T : struct, IExcelRow<T>
    {
        return func(dataManager.GetExcelSheet<T>().GetRow(rankId)).ToString();
    }


    private void Logout(int type, int code)
    {
        _retainerItemCache.Clear();
    }

    private void TerritoryChanged(ushort territoryType)
    {
        // there is no GC area that is in the same zone as a retainer bell, so this should be often enough.
        _retainerItemCache.Clear();
    }

    public unsafe void InteractWithTarget(IGameObject obj)
    {
        _pluginLog.Information($"Setting target to {obj}");
        if (_targetManager.Target == null || _targetManager.Target.EntityId != obj.EntityId)
        {
            _targetManager.Target = obj;
        }

        TargetSystem.Instance()->InteractWithObject(
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address, false);
    }

    public unsafe int GetCurrentSealCount()
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

    public unsafe GrandCompany GetGrandCompany() => (GrandCompany)PlayerState.Instance()->GrandCompany;

    public unsafe byte GetGrandCompanyRank() => PlayerState.Instance()->GetGrandCompanyRank();

    public float GetDistanceToNpc(int npcId, out IGameObject? o)
    {
        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind == ObjectKind.EventNpc && obj is ICharacter c)
            {
                if (c.DataId == npcId)
                {
                    o = obj;
                    return Vector3.Distance(_clientState.LocalPlayer?.Position ?? Vector3.Zero, c.Position);
                }
            }
        }

        o = null;
        return float.MaxValue;
    }

    public static int GetNpcId(IGameObject obj)
    {
        return Marshal.ReadInt32(obj.Address + 128);
    }

    public int GetPersonnelOfficerId()
    {
        return GetGrandCompany() switch
        {
            GrandCompany.Maelstrom => 0xF4B94,
            GrandCompany.ImmortalFlames => 0xF4B97,
            GrandCompany.TwinAdder => 0xF4B9A,
            _ => int.MaxValue,
        };
    }

    public int GetQuartermasterId()
    {
        return GetGrandCompany() switch
        {
            GrandCompany.Maelstrom => 0xF4B93,
            GrandCompany.ImmortalFlames => 0xF4B96,
            GrandCompany.TwinAdder => 0xF4B99,
            _ => int.MaxValue,
        };
    }

    public uint GetSealCap() => _gcRankInfo.TryGetValue(GetGrandCompanyRank(), out var rank) ? rank.MaxSeals : 0;

    public uint MaxSealCap => _gcRankInfo[11].MaxSeals;

    public uint GetSealsRequiredForNextRank()
        => _gcRankInfo.GetValueOrDefault(GetGrandCompanyRank())?.RequiredSeals ?? 0;

    public byte GetRequiredHuntingLogForNextRank()
        => _gcRankInfo.GetValueOrDefault(GetGrandCompanyRank() + 1u)?.RequiredHuntingLog ?? 0;

    public string? GetNextGrandCompanyRankName()
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

    public decimal GetSealMultiplier()
    {
        // priority seal allowance
        if (_clientState.LocalPlayer!.StatusList.Any(x => x.StatusId == 1078))
            return 1.15m;

        // seal sweetener 1/2
        var fcStatus = _clientState.LocalPlayer!.StatusList.FirstOrDefault(x => x.StatusId == 414);
        if (fcStatus != null)
        {
            return 1m + fcStatus.Param / 100m;
        }

        return 1;
    }

    /// <summary>
    /// This returns ALL items that can be turned in, regardless of filter settings.
    /// </summary>
    public unsafe List<TurnInItem> BuildTurnInList(AgentGrandCompanySupply* agent)
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

    public void Dispose()
    {
        _clientState.TerritoryChanged -= TerritoryChanged;
        _clientState.Logout -= Logout;
    }
}
