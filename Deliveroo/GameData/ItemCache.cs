using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.GeneratedSheets;

namespace Deliveroo.GameData;

internal sealed class ItemCache
{
    private readonly Dictionary<string, HashSet<uint>> _itemNamesToIds = new();

    public ItemCache(IDataManager dataManager)
    {
        foreach (var item in dataManager.GetExcelSheet<Item>()!)
        {
            string name = item.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (_itemNamesToIds.TryGetValue(name, out HashSet<uint>? itemIds))
                itemIds.Add(item.RowId);
            else
                _itemNamesToIds.Add(name, new HashSet<uint>{item.RowId});
        }
    }

    public HashSet<uint> GetItemIdFromItemName(string name) => 
        _itemNamesToIds.TryGetValue(name, out var itemIds) ? itemIds : [];
}
