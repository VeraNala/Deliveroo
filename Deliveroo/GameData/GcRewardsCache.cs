using System.Collections.Generic;
using System.Linq;
using Dalamud.Data;
using Lumina.Excel.GeneratedSheets;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Deliveroo.GameData;

internal sealed class GcRewardsCache
{
    public GcRewardsCache(DataManager dataManager)
    {
        var categories = dataManager.GetExcelSheet<GCScripShopCategory>()!
            .Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId,
                x =>
                    (GrandCompany: (GrandCompany)x.GrandCompany.Row,
                        Tier: (RewardTier)x.Tier,
                        SubCategory: (RewardSubCategory)x.SubCategory));

        var items = dataManager.GetExcelSheet<GCScripShopItem>()!
            .Where(x => x.RowId > 0 && x.Item.Row > 0)
            .ToList();

        foreach (var item in items)
        {
            var category = categories[item.RowId];
            Rewards[category.GrandCompany].Add(new GcRewardItem
            {
                ItemId = item.Item.Row,
                Name = item.Item.Value!.Name.ToString(),
                GrandCompany = category.GrandCompany,
                Tier = category.Tier,
                SubCategory = category.SubCategory,
                RequiredRank = item.RequiredGrandCompanyRank.Row,
                StackSize = item.Item!.Value.StackSize,
                SealCost = item.CostGCSeals,
            });
        }
    }

    public Dictionary<GrandCompany, List<GcRewardItem>> Rewards { get; } = new()
    {
        { GrandCompany.Maelstrom, new() },
        { GrandCompany.TwinAdder, new() },
        { GrandCompany.ImmortalFlames, new() }
    };

    public GcRewardItem GetReward(GrandCompany grandCompany, uint itemId)
        => Rewards[grandCompany].Single(x => x.ItemId == itemId);
}
