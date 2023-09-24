using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
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
    private readonly ConfigWindow _configWindow;

    public TurnInWindow(DeliverooPlugin plugin, DalamudPluginInterface pluginInterface, Configuration configuration,
        GcRewardsCache gcRewardsCache, ConfigWindow configWindow)
        : base("GC Delivery###DeliverooTurnIn")
    {
        _plugin = plugin;
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _gcRewardsCache = gcRewardsCache;
        _configWindow = configWindow;

        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(330, 50),
            MaximumSize = new Vector2(500, 999),
        };

        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
        ShowCloseButton = false;
    }

    public bool State { get; set; }
    public decimal Multiplier { private get; set; }
    public string Error { private get; set; } = string.Empty;

    public List<PurchaseItemRequest> SelectedItems
    {
        get
        {
            GrandCompany grandCompany = _plugin.GetGrandCompany();
            if (grandCompany == GrandCompany.None)
                return new List<PurchaseItemRequest>();

            var rank = _plugin.GetGrandCompanyRank();
            return _configuration.ItemsToPurchase
                .Where(x => x.ItemId != 0)
                .Select(x => new { Item = x, Reward = _gcRewardsCache.GetReward(grandCompany, x.ItemId) })
                .Where(x => x.Reward.RequiredRank <= rank)
                .Select(x => new PurchaseItemRequest
                {
                    ItemId = x.Item.ItemId,
                    Name = x.Reward.Name,
                    EffectiveLimit = CalculateEffectiveLimit(
                        x.Item.ItemId,
                        x.Item.Limit <= 0 ? uint.MaxValue : (uint)x.Item.Limit,
                        x.Reward.StackSize),
                    SealCost = x.Reward.SealCost,
                    Tier = x.Reward.Tier,
                    SubCategory = x.Reward.SubCategory,
                    StackSize = x.Reward.StackSize,
                })
                .ToList();
        }
    }

    public override void Draw()
    {
        GrandCompany grandCompany = _plugin.GetGrandCompany();
        if (grandCompany == GrandCompany.None)
        {
            // not sure we should ever get here
            State = false;
            return;
        }

        if (_plugin.GetGrandCompanyRank() < 6)
        {
            State = false;
            ImGui.TextColored(ImGuiColors.DalamudRed, "You do not have the required rank for Expert Delivery.");
            return;
        }

        bool state = State;
        if (ImGui.Checkbox("Handle GC turn ins/exchange automatically", ref state))
        {
            State = state;
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButton("###OpenConfig", FontAwesomeIcon.Cog))
            _configWindow.IsOpen = true;

        ImGui.Indent(27);
        if (!string.IsNullOrEmpty(Error))
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, Error);
        }
        else
        {
            if (Multiplier == 1m)
            {
                ImGui.TextColored(ImGuiColors.DalamudYellow, "You do not have an active seal buff.");
            }
            else
            {
                ImGui.TextColored(ImGuiColors.HealerGreen, $"Current Buff: {(Multiplier - 1m) * 100:N0}%%");
            }

            ImGui.Unindent(27);
            ImGui.Separator();
            ImGui.BeginDisabled(state);

            ImGui.Text("Items to buy:");
            DrawItemsToBuy(grandCompany);

            ImGui.EndDisabled();
        }

        ImGui.Separator();
        ImGui.Text($"Debug (State): {_plugin.CurrentStage}");
    }

    private void DrawItemsToBuy(GrandCompany grandCompany)
    {
        List<(uint ItemId, string Name, uint Rank)> comboValues = new()
            { (GcRewardItem.None.ItemId, GcRewardItem.None.Name, GcRewardItem.None.RequiredRank) };
        foreach (uint itemId in _configuration.ItemsAvailableForPurchase)
        {
            var gcReward = _gcRewardsCache.GetReward(grandCompany, itemId);
            int itemCount = _plugin.GetItemCount(itemId);
            if (itemCount > 0)
                comboValues.Add((itemId, $"{gcReward.Name} ({itemCount:N0})", gcReward.RequiredRank));
            else
                comboValues.Add((itemId, gcReward.Name, gcReward.RequiredRank));
        }

        if (_configuration.ItemsToPurchase.Count == 0)
            _configuration.ItemsToPurchase.Add(new Configuration.PurchasePriority
                { ItemId = GcRewardItem.None.ItemId, Limit = 0 });

        int? itemToRemove = null;
        for (int i = 0; i < _configuration.ItemsToPurchase.Count; ++i)
        {
            ImGui.PushID($"ItemToBuy{i}");
            var item = _configuration.ItemsToPurchase[i];
            int comboValueIndex = comboValues.FindIndex(x => x.ItemId == item.ItemId);
            if (comboValueIndex < 0)
            {
                item.ItemId = 0;
                item.Limit = 0;
                _pluginInterface.SavePluginConfig(_configuration);

                comboValueIndex = 0;
            }

            if (ImGui.Combo("", ref comboValueIndex, comboValues.Select(x => x.Name).ToArray(), comboValues.Count))
            {
                item.ItemId = comboValues[comboValueIndex].ItemId;
                _pluginInterface.SavePluginConfig(_configuration);
            }

            if (_configuration.ItemsToPurchase.Count >= 2)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Times))
                    itemToRemove = i;
            }

            ImGui.Indent(27);
            if (comboValueIndex > 0)
            {
                ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 130);
                int limit = item.Limit;
                if (item.ItemId == ItemIds.Venture)
                    limit = Math.Min(limit, 65_000);

                if (ImGui.InputInt("Maximum items to buy", ref limit, 50, 500))
                {
                    item.Limit = Math.Max(0, limit);
                    if (item.ItemId == ItemIds.Venture)
                        item.Limit = Math.Min(item.Limit, 65_000);

                    _pluginInterface.SavePluginConfig(_configuration);
                }
            }
            else if (item.Limit != 0)
            {
                item.Limit = 0;
                _pluginInterface.SavePluginConfig(_configuration);
            }

            if (comboValueIndex > 0 && comboValues[comboValueIndex].Rank > _plugin.GetGrandCompanyRank())
            {
                ImGui.TextColored(ImGuiColors.DalamudRed,
                    "This item will be skipped, your rank isn't high enough to buy it.");
            }

            ImGui.Unindent(27);
            ImGui.PopID();
        }

        if (itemToRemove != null)
        {
            _configuration.ItemsToPurchase.RemoveAt(itemToRemove.Value);
            _pluginInterface.SavePluginConfig(_configuration);
        }

        if (_configuration.ItemsAvailableForPurchase.Any(x => _configuration.ItemsToPurchase.All(y => x != y.ItemId)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add Item"))
            {
                _configuration.ItemsToPurchase.Add(new Configuration.PurchasePriority
                    { ItemId = GcRewardItem.None.ItemId, Limit = 0 });
                _pluginInterface.SavePluginConfig(_configuration);
            }
        }
    }

    private unsafe uint CalculateEffectiveLimit(uint itemId, uint limit, uint stackSize)
    {
        if (itemId == ItemIds.Venture)
            return Math.Min(limit, 65_000);
        else
        {
            uint slotsThatCanBeUsed = 0;
            InventoryManager* inventoryManager = InventoryManager.Instance();
            for (InventoryType inventoryType = InventoryType.Inventory1;
                 inventoryType <= InventoryType.Inventory4;
                 ++inventoryType)
            {
                var container = inventoryManager->GetInventoryContainer(inventoryType);
                for (int i = 0; i < container->Size; ++i)
                {
                    var item = container->GetInventorySlot(i);
                    if (item == null || item->ItemID == 0 || item->ItemID == itemId)
                    {
                        slotsThatCanBeUsed++;
                    }
                }
            }

            return Math.Min(limit, slotsThatCanBeUsed * stackSize);
        }
    }
}
