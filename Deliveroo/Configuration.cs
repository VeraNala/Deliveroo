using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text;
using Deliveroo.GameData;
using LLib.ImGui;

namespace Deliveroo;

internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    [Obsolete]
    public List<uint> ItemsAvailableForPurchase { get; set; } = [];

    public List<PurchaseOption> ItemsAvailableToPurchase { get; set; } = [];
    public List<PurchasePriority> ItemsToPurchase { get; set; } = [];

    public int ReservedSealCount { get; set; }
    public bool ReserveDifferentSealCountAtMaxRank { get; set; }
    public int ReservedSealCountAtMaxRank { get; set; }
    public XivChatType ChatType { get; set; } = XivChatType.Debug;
    public int PauseAtRank { get; set; }
    public EBehaviorOnOtherWorld BehaviorOnOtherWorld { get; set; } = EBehaviorOnOtherWorld.Warning;
    public bool DisableFrameLimiter { get; set; } = true;
    public bool UncapFrameRate { get; set; }
    public VirtualKey QuickTurnInKey { get; set; } = VirtualKey.SHIFT;

    public WindowConfig TurnInWindowConfig { get; } = new();
    public WindowConfig ConfigWindowConfig { get; } = new();

    internal sealed class PurchaseOption
    {
        public uint ItemId { get; set; }
        public bool SameQuantityForAllLists { get; set; }
        public int GlobalLimit { get; set; }
    }

    internal sealed class PurchasePriority
    {
        [JsonIgnore]
        public Guid InternalId { get; } = Guid.NewGuid();

        public uint ItemId { get; set; }
        public int Limit { get; set; }
        public bool Enabled { get; set; } = true;
        public PurchaseType Type { get; set; } = PurchaseType.KeepStocked;
        public bool CheckRetainerInventory { get; set; }

        public string GetIcon()
        {
            return Type switch
            {
                PurchaseType.PurchaseOneTime => SeIconChar.BoxedNumber1.ToIconString(),
                PurchaseType.KeepStocked => SeIconChar.Circle.ToIconString(),
                _ => SeIconChar.BoxedQuestionMark.ToIconString(),
            };
        }
    }

    public enum PurchaseType
    {
        PurchaseOneTime,
        KeepStocked,
    }

    public bool AddVentureIfNoItemToPurchaseSelected()
    {
        if (ItemsAvailableToPurchase.Count == 0)
        {
            ItemsAvailableToPurchase.Add(new PurchaseOption { ItemId = ItemIds.Venture });
            return true;
        }

        return false;
    }

    public enum EBehaviorOnOtherWorld
    {
        None,
        Warning,
        DisableTurnIn,
    }
}
