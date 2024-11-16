using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Deliveroo.GameData;

internal sealed class GcRewardsCache
{
    public GcRewardsCache(IDataManager dataManager)
    {
        var categories = dataManager.GetExcelSheet<GCScripShopCategory>()
            .Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId,
                x =>
                    (GrandCompany: (GrandCompany)x.GrandCompany.RowId,
                        Tier: (RewardTier)x.Tier,
                        SubCategory: (RewardSubCategory)x.SubCategory));

        Rewards = dataManager.GetSubrowExcelSheet<GCScripShopItem>()
            .SelectMany(x => x)
            .Where(x => x.RowId > 0 && x.Item.RowId > 0)
            .GroupBy(item =>
            {
                var category = categories[item.RowId];
                return new
                {
                    ItemId = item.Item.RowId,
                    Name = item.Item.Value.Name.ToString(),
                    IconId = item.Item.RowId == ItemIds.Venture ? 25917 : item.Item.Value.Icon,
                    category.Tier,
                    category.SubCategory,
                    RequiredRank = item.RequiredGrandCompanyRank.RowId,
                    item.Item.Value.StackSize,
                    SealCost = item.CostGCSeals,
                    InventoryLimit = item.Item.Value.IsUnique ? 1
                        : item.Item.RowId == ItemIds.Venture ? item.Item.Value.StackSize
                        : int.MaxValue,
                };
            })
            .Select(item => new GcRewardItem
            {
                ItemId = item.Key.ItemId,
                Name = item.Key.Name,
                IconId = (ushort)item.Key.IconId,
                Tier = item.Key.Tier,
                SubCategory = item.Key.SubCategory,
                RequiredRank = item.Key.RequiredRank,
                StackSize = item.Key.StackSize,
                SealCost = item.Key.SealCost,
                GrandCompanies = item.Select(x => categories[x.RowId].GrandCompany)
                    .ToList()
                    .AsReadOnly(),
                InventoryLimit = item.Key.InventoryLimit,
            })
            .ToList()
            .AsReadOnly();
        RewardLookup = Rewards.ToDictionary(x => x.ItemId).AsReadOnly();
    }

    public IReadOnlyList<GcRewardItem> Rewards { get; }
    public IReadOnlyDictionary<uint, GcRewardItem> RewardLookup { get; }

    public GcRewardItem GetReward(uint itemId) => RewardLookup[itemId];
}
