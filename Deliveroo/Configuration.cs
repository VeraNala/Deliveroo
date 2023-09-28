using System.Collections.Generic;
using Dalamud.Configuration;
using Deliveroo.GameData;

namespace Deliveroo;

internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<uint> ItemsAvailableForPurchase { get; set; } = new();
    public List<PurchasePriority> ItemsToPurchase { get; set; } = new();

    public int ReservedSealCount { get; set; } = 0;

    /// <summary>
    /// A config-only setting, not exposed in the UI.
    ///
    /// If set, buys all GC items in their max quantity (otherwise, everything except ventures is capped to 99).
    /// </summary>
    public bool IgnoreCertainLimitations { get; set; } = false;

    internal sealed class PurchasePriority
    {
        public uint ItemId { get; set; }
        public int Limit { get; set; }
    }

    public bool AddVentureIfNoItemToPurchaseSelected()
    {
        if (ItemsAvailableForPurchase.Count == 0)
        {
            ItemsAvailableForPurchase.Add(ItemIds.Venture);
            return true;
        }

        return false;
    }
}
