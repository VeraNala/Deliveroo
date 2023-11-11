using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using LLib;

namespace Deliveroo.Windows;

internal sealed class TurnInWindow : LImGui.LWindow
{
    private readonly DeliverooPlugin _plugin;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly ICondition _condition;
    private readonly GcRewardsCache _gcRewardsCache;

    public TurnInWindow(DeliverooPlugin plugin, DalamudPluginInterface pluginInterface, Configuration configuration,
        ICondition condition, GcRewardsCache gcRewardsCache, ConfigWindow configWindow)
        : base("GC Delivery###DeliverooTurnIn")
    {
        _plugin = plugin;
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _condition = condition;
        _gcRewardsCache = gcRewardsCache;

        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(330, 50),
            MaximumSize = new Vector2(500, 999),
        };

        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
        ShowCloseButton = false;
        AllowClickthrough = false;

        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ => configWindow.IsOpen = true,
            Priority = int.MinValue,
            ShowTooltip = () =>
            {
                ImGui.BeginTooltip();
                ImGui.Text("Open Configuration");
                ImGui.EndTooltip();
            }
        });
    }

    public bool State { get; set; }
    public decimal Multiplier { private get; set; }
    public string Error { private get; set; } = string.Empty;

    private bool UseCharacterSpecificItemsToPurchase =>
        _plugin.CharacterConfiguration is { OverrideItemsToPurchase: true };

    private IItemsToPurchase ItemsWrapper => UseCharacterSpecificItemsToPurchase
        ? new CharacterSpecificItemsToPurchase(_plugin.CharacterConfiguration!, _pluginInterface)
        : new GlobalItemsToPurchase(_configuration, _pluginInterface);

    public List<PurchaseItemRequest> SelectedItems
    {
        get
        {
            GrandCompany grandCompany = _plugin.GetGrandCompany();
            if (grandCompany == GrandCompany.None)
                return new List<PurchaseItemRequest>();

            var rank = _plugin.GetGrandCompanyRank();
            return ItemsWrapper.GetItemsToPurchase()
                .Where(x => x.ItemId != GcRewardItem.None.ItemId)
                .Where(x => x.Enabled)
                .Select(x => new { Item = x, Reward = _gcRewardsCache.GetReward(x.ItemId) })
                .Where(x => x.Reward.GrandCompanies.Contains(grandCompany))
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

    public override unsafe void Draw()
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

            if (Multiplier <= 1.10m)
            {
                InventoryManager* inventoryManager = InventoryManager.Instance();
                if (inventoryManager->GetInventoryItemCount(ItemIds.PrioritySealAllowance) > 0)
                {
                    ImGui.BeginDisabled(_condition[ConditionFlag.OccupiedInQuestEvent] || _condition[ConditionFlag.Casting]);
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bolt, "Use Priority Seal Allowance (15%)"))
                    {
                        AgentInventoryContext.Instance()->UseItem(ItemIds.PrioritySealAllowance);
                    }
                    ImGui.EndDisabled();
                }
            }

            ImGui.Unindent(27);
            ImGui.Separator();
            ImGui.BeginDisabled(state);

            DrawItemsToBuy(grandCompany);

            ImGui.EndDisabled();
        }

        ImGui.Separator();
        ImGui.Text($"Debug (State): {_plugin.CurrentStage}");
    }

    private void DrawItemsToBuy(GrandCompany grandCompany)
    {
        var itemsWrapper = ItemsWrapper;
        ImGui.Text($"Items to buy ({itemsWrapper.Name}):");

        List<(uint ItemId, string Name, IReadOnlyList<GrandCompany> GrandCompanies, uint Rank)> comboValues = new()
        {
            (GcRewardItem.None.ItemId, GcRewardItem.None.Name, new List<GrandCompany>(), GcRewardItem.None.RequiredRank)
        };
        foreach (uint itemId in _configuration.ItemsAvailableForPurchase)
        {
            var gcReward = _gcRewardsCache.GetReward(itemId);
            int itemCount = _plugin.GetItemCount(itemId);
            string itemName = gcReward.Name;
            if (itemCount > 0)
                itemName += $" ({itemCount:N0})";
            comboValues.Add((itemId, itemName, gcReward.GrandCompanies, gcReward.RequiredRank));
        }

        if (itemsWrapper.GetItemsToPurchase().Count == 0)
        {
            itemsWrapper.Add(new Configuration.PurchasePriority { ItemId = GcRewardItem.None.ItemId, Limit = 0 });
            itemsWrapper.Save();
        }

        int? itemToRemove = null;
        Configuration.PurchasePriority? itemToAdd = null;
        int indexToAdd = 0;
        for (int i = 0; i < itemsWrapper.GetItemsToPurchase().Count; ++i)
        {
            ImGui.PushID($"ItemToBuy{i}");
            var item = itemsWrapper.GetItemsToPurchase()[i];
            bool enabled = item.Enabled;
            ImGui.PushID($"Enable{i}");
            if (ImGui.Checkbox("", ref enabled))
            {
                item.Enabled = enabled;
                itemsWrapper.Save();
            }
            ImGui.PopID();

            ImGui.SameLine(0, 3);
            ImGui.BeginDisabled(!enabled);
            int comboValueIndex = comboValues.FindIndex(x => x.ItemId == item.ItemId);
            if (comboValueIndex < 0)
            {
                item.ItemId = 0;
                item.Limit = 0;
                itemsWrapper.Save();

                comboValueIndex = 0;
            }

            if (ImGui.Combo("", ref comboValueIndex, comboValues.Select(x => x.Name).ToArray(), comboValues.Count))
            {
                item.ItemId = comboValues[comboValueIndex].ItemId;
                itemsWrapper.Save();
            }

            ImGui.EndDisabled();

            if (itemsWrapper.GetItemsToPurchase().Count >= 2)
            {
                ImGui.SameLine();
                if (ImGuiComponents.IconButton($"##Up{i}", FontAwesomeIcon.ArrowUp))
                {
                    itemToAdd = item;
                    if (i > 0)
                        indexToAdd = i - 1;
                    else
                        indexToAdd = itemsWrapper.GetItemsToPurchase().Count - 1;
                }

                ImGui.SameLine(0, 0);
                if (ImGuiComponents.IconButton($"##Down{i}", FontAwesomeIcon.ArrowDown))
                {
                    itemToAdd = item;
                    if (i < itemsWrapper.GetItemsToPurchase().Count - 1)
                        indexToAdd = i + 1;
                    else
                        indexToAdd = 0;
                }

                ImGui.SameLine();
                if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Times))
                    itemToRemove = i;
            }

            if (enabled)
            {
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

                        itemsWrapper.Save();
                    }
                }
                else if (item.Limit != 0)
                {
                    item.Limit = 0;
                    itemsWrapper.Save();
                }

                if (comboValueIndex > 0)
                {
                    var comboItem = comboValues[comboValueIndex];
                    if (!comboItem.GrandCompanies.Contains(grandCompany))
                    {
                        ImGui.TextColored(ImGuiColors.DalamudRed,
                            "This item will be skipped, as you are in the wrong Grand Company.");
                    }
                    else if (comboItem.Rank > _plugin.GetGrandCompanyRank())
                    {
                        ImGui.TextColored(ImGuiColors.DalamudRed,
                            "This item will be skipped, your rank isn't high enough to buy it.");
                    }
                }

                ImGui.Unindent(27);
            }

            ImGui.PopID();
        }

        if (itemToAdd != null)
        {
            itemsWrapper.Remove(itemToAdd);
            itemsWrapper.Insert(indexToAdd, itemToAdd);
            itemsWrapper.Save();
        }

        if (itemToRemove != null)
        {
            itemsWrapper.RemoveAt(itemToRemove.Value);
            itemsWrapper.Save();
        }

        if (_configuration.ItemsAvailableForPurchase.Any(x =>
                itemsWrapper.GetItemsToPurchase().All(y => x != y.ItemId)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add Item"))
                ImGui.OpenPopup("##AddItem");

            if (ImGui.BeginPopupContextItem("##AddItem", ImGuiPopupFlags.NoOpenOverItems))
            {
                foreach (var itemId in _configuration.ItemsAvailableForPurchase.Distinct())
                {
                    if (_gcRewardsCache.RewardLookup.TryGetValue(itemId, out var reward))
                    {
                        if (ImGui.MenuItem($"{reward.Name}##{itemId}"))
                        {
                            itemsWrapper.Add(new Configuration.PurchasePriority { ItemId = itemId, Limit = 0 });
                            itemsWrapper.Save();
                            ImGui.CloseCurrentPopup();

                        }
                    }
                }

                ImGui.EndPopup();
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

    private interface IItemsToPurchase
    {
        string Name { get; }

        IReadOnlyList<Configuration.PurchasePriority> GetItemsToPurchase();
        void Add(Configuration.PurchasePriority purchasePriority);
        void Insert(int index, Configuration.PurchasePriority purchasePriority);
        void Remove(Configuration.PurchasePriority purchasePriority);
        void RemoveAt(int index);
        void Save();
    }

    private sealed class CharacterSpecificItemsToPurchase : IItemsToPurchase
    {
        private readonly CharacterConfiguration _characterConfiguration;
        private readonly DalamudPluginInterface _pluginInterface;

        public CharacterSpecificItemsToPurchase(CharacterConfiguration characterConfiguration,
            DalamudPluginInterface pluginInterface)
        {
            _characterConfiguration = characterConfiguration;
            _pluginInterface = pluginInterface;
        }

        public string Name => _characterConfiguration.CachedPlayerName ?? "?";

        public IReadOnlyList<Configuration.PurchasePriority> GetItemsToPurchase()
            => _characterConfiguration.ItemsToPurchase;

        public void Add(Configuration.PurchasePriority purchasePriority)
            => _characterConfiguration.ItemsToPurchase.Add(purchasePriority);

        public void Insert(int index, Configuration.PurchasePriority purchasePriority)
            => _characterConfiguration.ItemsToPurchase.Insert(index, purchasePriority);

        public void Remove(Configuration.PurchasePriority purchasePriority)
            => _characterConfiguration.ItemsToPurchase.Remove(purchasePriority);

        public void RemoveAt(int index)
            => _characterConfiguration.ItemsToPurchase.RemoveAt(index);

        public void Save()
            => _characterConfiguration.Save(_pluginInterface);
    }

    private sealed class GlobalItemsToPurchase : IItemsToPurchase
    {
        private readonly Configuration _configuration;
        private readonly DalamudPluginInterface _pluginInterface;

        public GlobalItemsToPurchase(Configuration configuration, DalamudPluginInterface pluginInterface)
        {
            _configuration = configuration;
            _pluginInterface = pluginInterface;
        }

        public string Name => "all characters";

        public IReadOnlyList<Configuration.PurchasePriority> GetItemsToPurchase()
            => _configuration.ItemsToPurchase;

        public void Add(Configuration.PurchasePriority purchasePriority)
            => _configuration.ItemsToPurchase.Add(purchasePriority);

        public void Insert(int index, Configuration.PurchasePriority purchasePriority)
            => _configuration.ItemsToPurchase.Insert(index, purchasePriority);

        public void Remove(Configuration.PurchasePriority purchasePriority)
            => _configuration.ItemsToPurchase.Remove(purchasePriority);

        public void RemoveAt(int index)
            => _configuration.ItemsToPurchase.RemoveAt(index);

        public void Save()
            => _pluginInterface.SavePluginConfig(_configuration);
    }
}
