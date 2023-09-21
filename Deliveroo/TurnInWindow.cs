using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;

namespace Deliveroo;

internal sealed class TurnInWindow : Window
{
    private int _sealPurchaseSelected = 0;

    private readonly List<SealPurchaseOption> _sealPurchaseOptions = new()
    {
        new SealPurchaseOption(0, "Buy Nothing", SealPurchaseOption.PurchaseRank.Unknown,
            SealPurchaseOption.PurchaseType.Unknown, 0, 0),
        new SealPurchaseOption(21072, "Venture", SealPurchaseOption.PurchaseRank.First,
            SealPurchaseOption.PurchaseType.Materiel, 0, 200),
        new SealPurchaseOption(5530, "Coke", SealPurchaseOption.PurchaseRank.Third,
            SealPurchaseOption.PurchaseType.Materials, 31, 200),
    };

    public TurnInWindow()
        : base("GC Delivery###DeliverooTurnIn")
    {
        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;

        Flags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    public bool State { get; set; }
    public decimal Multiplier { private get; set; }
    public SealPurchaseOption SelectedOption => _sealPurchaseOptions[_sealPurchaseSelected];

    public string Debug { get; set; }

    public override void Draw()
    {
        bool state = State;
        if (ImGui.Checkbox("Handle GC turn ins/exchange automatically", ref state))
        {
            State = state;
        }

        ImGui.Indent(27);
        if (Multiplier == 1m)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, "You do not have a buff active");
        }
        else
        {
            ImGui.TextColored(ImGuiColors.HealerGreen, $"Current Buff: {(Multiplier - 1m) * 100:N0}%%");
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(state);
        string[] comboValues = _sealPurchaseOptions.Select(x =>
        {
            if (x.ItemId == 0)
                return x.Name;

            return $"{x.Name} ({GetItemCount(x.ItemId):N0}x)";
        }).ToArray();
        ImGui.Combo("", ref _sealPurchaseSelected, comboValues, comboValues.Length);
        ImGui.EndDisabled();

        ImGui.Unindent(27);

        ImGui.Separator();
        ImGui.Text($"Debug (State): {Debug}");
    }

    private unsafe int GetItemCount(uint itemId)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager->GetInventoryItemCount(itemId, false, false, false);
    }
}
