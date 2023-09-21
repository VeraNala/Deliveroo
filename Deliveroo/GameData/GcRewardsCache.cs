using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Linq;
using Dalamud;
using Dalamud.Data;
using Dalamud.Logging;
using Lumina.Excel.GeneratedSheets;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace Deliveroo.GameData;

internal class GcRewardsCache
{
    public GcRewardsCache(DataManager dataManager)
    {
        var categories = dataManager.GetExcelSheet<GCScripShopCategory>()!
            .Where(x => x.RowId > 0)
            .ToDictionary(x => x.RowId,
                x =>
                    (Gc: (GrandCompany)x.GrandCompany.Row,
                        Tier: (RewardTier)x.Tier,
                        SubCategory: (RewardSubCategory)x.SubCategory));

        var items = dataManager.GetExcelSheet<GCScripShopItem>()!
            .Where(x => x.RowId > 0 && x.Item.Row > 0)
            .ToList();

        foreach (var item in items)
        {
            var category = categories[item.RowId];
            Rewards[category.Gc].Add(new GcRewardItem
            {
                ItemId = item.Item.Row,
                Name = item.Item.Value!.Name.ToString(),
                GrandCompany = category.Gc,
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
}
