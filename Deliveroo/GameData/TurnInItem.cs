using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.String;

namespace Deliveroo.GameData;

internal sealed class TurnInItem
{
    public required int ItemId { get; init; }
    public required string Name { get; init; }
    public required int SealsWithBonus { get; init; }
    public required int SealsWithoutBonus { get; init; }
    public required byte ItemUiCategory { get; init; }
}
