namespace Deliveroo;

internal sealed class SealPurchaseOption
{
    public SealPurchaseOption(uint itemId, string name, PurchaseRank rank, PurchaseType type, int position,
        int sealCost)
    {
        ItemId = itemId;
        Name = name;
        Rank = rank;
        Type = type;
        Position = position;
        SealCost = sealCost;
    }

    public uint ItemId { get; }
    public string Name { get; }
    public PurchaseRank Rank { get; }
    public PurchaseType Type { get; }
    public int Position { get; }
    public int SealCost { get; }

    public enum PurchaseRank : int
    {
        First = 0,
        Second = 1,
        Third = 2,
        Unknown = int.MaxValue,
    }

    public enum PurchaseType : int
    {
        Materiel = 1,
        Weapons = 2,
        Armor = 3,
        Materials = 4,
        Unknown = int.MaxValue,
    }
}
