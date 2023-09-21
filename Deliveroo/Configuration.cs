using System.Collections.Generic;
using Dalamud.Configuration;

namespace Deliveroo;

internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<uint> ItemsAvailableForPurchase { get; set; } = new();
    public uint SelectedPurchaseItemId { get; set; } = 0;
}
