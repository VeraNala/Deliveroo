using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Plugin;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;

namespace Deliveroo.Windows;

internal sealed class TurnInWindow : Window
{
    private readonly DeliverooPlugin _plugin;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly GcRewardsCache _gcRewardsCache;
    private int _selectedAutoBuyItem = 0;

    public TurnInWindow(DeliverooPlugin plugin, DalamudPluginInterface pluginInterface, Configuration configuration,
        GcRewardsCache gcRewardsCache)
        : base("GC Delivery###DeliverooTurnIn")
    {
        _plugin = plugin;
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _gcRewardsCache = gcRewardsCache;

        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;

        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
        ShowCloseButton = false;
    }

    public bool State { get; set; }
    public decimal Multiplier { private get; set; }

    public uint SelectedItemId
    {
        get
        {
            if (_selectedAutoBuyItem == 0 || _selectedAutoBuyItem > _configuration.ItemsAvailableForPurchase.Count)
                return 0;

            return _configuration.ItemsAvailableForPurchase[_selectedAutoBuyItem - 1];
        }
        set
        {
            int index = _configuration.ItemsAvailableForPurchase.IndexOf(value);
            if (index >= 0)
                _selectedAutoBuyItem = index + 1;
            else
                _selectedAutoBuyItem = 0;
        }
    }

    public GcRewardItem SelectedItem
    {
        get
        {
            uint selectedItemId = SelectedItemId;
            if (selectedItemId == 0)
                return GcRewardItem.None;

            return _gcRewardsCache.Rewards[_plugin.GetGrandCompany()].Single(x => x.ItemId == selectedItemId);
        }
    }

    public override void OnOpen()
    {
        SelectedItemId = _configuration.SelectedPurchaseItemId;
    }

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

        GrandCompany grandCompany = _plugin.GetGrandCompany();
        if (grandCompany != GrandCompany.None) // not sure we should ever get here
        {
            ImGui.Spacing();
            ImGui.BeginDisabled(state);

            List<string> comboValues = new() { GcRewardItem.None.Name };
            foreach (var itemId in _configuration.ItemsAvailableForPurchase)
            {
                var name = _gcRewardsCache.Rewards[grandCompany].First(x => x.ItemId == itemId).Name;
                int itemCount = GetItemCount(itemId);
                if (itemCount > 0)
                    comboValues.Add($"{name} ({itemCount:N0})");
                else
                    comboValues.Add(name);
            }

            if (ImGui.Combo("", ref _selectedAutoBuyItem, comboValues.ToArray(), comboValues.Count))
            {
                _configuration.SelectedPurchaseItemId = SelectedItemId;
                _pluginInterface.SavePluginConfig(_configuration);
            }

            if (SelectedItem.IsValid() && SelectedItem.RequiredRank > _plugin.GetGrandCompanyRank())
                ImGui.TextColored(ImGuiColors.DalamudRed, "Your rank isn't high enough to buy this item.");

            ImGui.EndDisabled();
        }

        ImGui.Unindent(27);

        ImGui.Separator();
        ImGui.Text($"Debug (State): {_plugin.CurrentStage}");
    }

    private unsafe int GetItemCount(uint itemId)
    {
        InventoryManager* inventoryManager = InventoryManager.Instance();
        return inventoryManager->GetInventoryItemCount(itemId, false, false, false);
    }
}
