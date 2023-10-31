namespace Deliveroo.GameData;

internal sealed class TurnInItem
{
    public required uint ItemId { get; init; }
    public required string Name { get; init; }
    public required int SealsWithBonus { get; init; }
    public required int SealsWithoutBonus { get; init; }
    public required byte ItemUiCategory { get; init; }
}
