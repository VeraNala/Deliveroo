using Deliveroo.GameData;

namespace Deliveroo;

internal sealed class PurchaseItemRequest
{
    public required uint ItemId { get; init; }
    public required string Name { get; set; }
    public required uint EffectiveLimit { get; init; }
    public required uint SealCost { get; init; }
    public required RewardTier Tier { get; init; }
    public required RewardSubCategory SubCategory { get; init; }
    public required uint StackSize { get; init; }
}
