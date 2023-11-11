using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Deliveroo.GameData;

internal sealed class GcRewardItem : IEquatable<GcRewardItem>
{
    public static GcRewardItem None { get; } = new GcRewardItem
    {
        ItemId = 0,
        Name = "---",
        IconId = 0,
        GrandCompanies = new List<GrandCompany>().AsReadOnly(),
        Tier = RewardTier.First,
        SubCategory = RewardSubCategory.Unknown,
        RequiredRank = 0,
        StackSize = 0,
        SealCost = 100_000,
    };

    public required uint ItemId { get; init; }
    public required string Name { get; init; }
    public required ushort IconId { get; init; }
    public required IReadOnlyList<GrandCompany> GrandCompanies { get; init; }
    public required RewardTier Tier { get; init; }
    public required RewardSubCategory SubCategory { get; init; }
    public required uint RequiredRank { get; init; }
    public required uint StackSize { get; init; }
    public required uint SealCost { get; init; }

    public bool IsValid() => ItemId > 0 && GrandCompanies.Count > 0;
    public bool Limited => GrandCompanies.Count < 3;

    public bool Equals(GcRewardItem? other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ItemId == other.ItemId;
    }

    public override bool Equals(object? obj)
    {
        return ReferenceEquals(this, obj) || obj is GcRewardItem other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (int)ItemId;
    }

    public static bool operator ==(GcRewardItem? left, GcRewardItem? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(GcRewardItem? left, GcRewardItem? right)
    {
        return !Equals(left, right);
    }
}
