using System.Collections.Generic;
using Dalamud.Configuration;

namespace Deliveroo;

internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<uint> ItemsAvailableForPurchase { get; set; } = new();
    public List<PurchasePriority> ItemsToPurchase { get; set; } = new();

    public int ReservedSealCount { get; set; } = 0;
    public bool IgnoreCertainLimitations { get; set; } = false;

    internal sealed class PurchasePriority
    {
        public uint ItemId { get; set; }
        public int Limit { get; set; }
    }

}
