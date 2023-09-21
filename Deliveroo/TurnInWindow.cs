using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace Deliveroo;

internal sealed class TurnInWindow : Window
{
    public TurnInWindow()
        : base("Turn In###DeliverooTurnIn")
    {
        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;

        Flags = ImGuiWindowFlags.AlwaysAutoResize;
    }

    public bool State { get; set; }
    public decimal Multiplier { get; set; }

    public int CurrentVentureCount { get; set; }

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
        int current = 0;
        ImGui.Combo("", ref current, new string[] { $"Ventures ({CurrentVentureCount:N0})" }, 1);

        ImGui.Unindent(27);

        ImGui.Separator();
        ImGui.Text($"Debug (State): {Debug}");
    }
}
