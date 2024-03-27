using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;
using LLib;
using LLib.ImGui;

namespace Deliveroo.Windows;

internal sealed class TurnInWindow : LWindow
{
    private static readonly IReadOnlyList<InventoryType> InventoryTypes = new[]
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.EquippedItems
    }.AsReadOnly();

    private static readonly string[] StockingTypeLabels = { "Purchase Once", "Keep in Stock" };

    private readonly DeliverooPlugin _plugin;
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly GcRewardsCache _gcRewardsCache;
    private readonly ConfigWindow _configWindow;
    private readonly IconCache _iconCache;

    private bool _state;

    public TurnInWindow(DeliverooPlugin plugin, DalamudPluginInterface pluginInterface, Configuration configuration,
        ICondition condition, IClientState clientState, GcRewardsCache gcRewardsCache, ConfigWindow configWindow,
        IconCache iconCache)
        : base("GC Delivery###DeliverooTurnIn")
    {
        _plugin = plugin;
        _pluginInterface = pluginInterface;
        _configuration = configuration;
        _condition = condition;
        _clientState = clientState;
        _gcRewardsCache = gcRewardsCache;
        _configWindow = configWindow;
        _iconCache = iconCache;

        Position = new Vector2(100, 100);
        PositionCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(330, 50),
            MaximumSize = new Vector2(500, 999),
        };

        State = false;
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

    public bool State
    {
        get => _state;
        set
        {
            _state = value;
            if (value)
                Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
            else
                Flags = ImGuiWindowFlags.AlwaysAutoResize;
        }
    }
    public decimal Multiplier { private get; set; }
    public string Error { private get; set; } = string.Empty;

    private bool UseCharacterSpecificItemsToPurchase =>
        _plugin.CharacterConfiguration is { OverrideItemsToPurchase: true };

    private bool IsOnHomeWorld =>
        _clientState.LocalPlayer == null ||
        _clientState.LocalPlayer.HomeWorld.Id == _clientState.LocalPlayer.CurrentWorld.Id;

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
                .Where(x => x.Type == Configuration.PurchaseType.KeepStocked || x.Limit > 0)
                .Select(x => new { Item = x, Reward = _gcRewardsCache.GetReward(x.ItemId) })
                .Where(x => x.Reward.GrandCompanies.Contains(grandCompany))
                .Where(x => x.Reward.RequiredRank <= rank)
                .Select(x =>
                {
                    var request = new PurchaseItemRequest
                    {
                        ItemId = x.Item.ItemId,
                        Name = x.Reward.Name,
                        EffectiveLimit = CalculateEffectiveLimit(
                            x.Item.ItemId,
                            x.Item.Limit <= 0 ? uint.MaxValue : (uint)x.Item.Limit,
                            x.Reward.StackSize,
                            x.Reward.InventoryLimit),
                        SealCost = x.Reward.SealCost,
                        Tier = x.Reward.Tier,
                        SubCategory = x.Reward.SubCategory,
                        StackSize = x.Reward.StackSize,
                        Type = x.Item.Type,
                        CheckRetainerInventory = x.Item.CheckRetainerInventory,
                    };
                    if (x.Item.Type == Configuration.PurchaseType.PurchaseOneTime)
                    {
                        request.OnPurchase = qty =>
                        {
                            request.EffectiveLimit -= (uint)qty;
                            x.Item.Limit -= qty;
                            ItemsWrapper.Save();
                        };
                    }

                    return request;
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
        else if (_configuration.BehaviorOnOtherWorld == Configuration.EBehaviorOnOtherWorld.DisableTurnIn &&
                 !IsOnHomeWorld)
        {
            State = false;
            ImGui.TextColored(ImGuiColors.DalamudRed, "You are not on your home world.");
            return;
        }

        bool state = State;
        if (ImGui.Checkbox("Handle GC turn ins/exchange automatically", ref state))
        {
            State = state;
        }

        float indentSize = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.Indent(indentSize);
        if (!string.IsNullOrEmpty(Error))
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, Error);
        }
        else
        {
            if (_configuration.BehaviorOnOtherWorld == Configuration.EBehaviorOnOtherWorld.Warning && !IsOnHomeWorld)
            {
                ImGui.TextColored(ImGuiColors.DalamudRed,
                    "Turn-In disabled, you are not on your home world and will not earn FC points.");
            }

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
                AgentInventoryContext* agentInventoryContext = AgentInventoryContext.Instance();
                if (inventoryManager->GetInventoryItemCount(ItemIds.PrioritySealAllowance) > 0)
                {
                    ImGui.BeginDisabled(_condition[ConditionFlag.OccupiedInQuestEvent] ||
                                        _condition[ConditionFlag.Casting] ||
                                        agentInventoryContext == null);
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bolt, "Use Priority Seal Allowance (15%)"))
                    {
                        agentInventoryContext->UseItem(ItemIds.PrioritySealAllowance);
                    }

                    ImGui.EndDisabled();
                }
            }

            ImGui.Unindent(indentSize);
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

        List<(GcRewardItem Item, string NameWithoutRetainers, string NameWithRetainers)> comboValues = new()
        {
            (GcRewardItem.None, GcRewardItem.None.Name, GcRewardItem.None.Name),
        };
        foreach (uint itemId in _configuration.ItemsAvailableForPurchase)
        {
            var gcReward = _gcRewardsCache.GetReward(itemId);
            int itemCountWithoutRetainers = _plugin.GetItemCount(itemId, false);
            int itemCountWithRetainers = _plugin.GetItemCount(itemId, true);
            string itemNameWithoutRetainers = gcReward.Name;
            string itemNameWithRetainers = gcReward.Name;
            if (itemCountWithoutRetainers > 0)
                itemNameWithoutRetainers += $" ({itemCountWithoutRetainers:N0})";
            if (itemCountWithRetainers > 0)
                itemNameWithRetainers += $" ({itemCountWithRetainers:N0})";
            comboValues.Add((gcReward, itemNameWithoutRetainers, itemNameWithRetainers));
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
            Configuration.PurchasePriority item = itemsWrapper.GetItemsToPurchase()[i];

            float indentX = ImGui.GetCursorPosX();
            bool enabled = item.Enabled;
            int popColors = 0;
            if (!enabled)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.5f, 0.35f, 1f));
                popColors++;
            }

            if (ImGui.Button($"{item.GetIcon()}"))
                ImGui.OpenPopup($"Configure{i}");

            ImGui.PopStyleColor(popColors);

            if (ImGui.BeginPopup($"Configure{i}"))
            {
                if (ImGui.Checkbox($"Enabled##Enabled{i}", ref enabled))
                {
                    item.Enabled = enabled;
                    itemsWrapper.Save();
                }

                ImGui.SetNextItemWidth(375 * ImGuiHelpers.GlobalScale);
                int type = (int)item.Type;
                if (ImGui.Combo($"##Type{i}", ref type, StockingTypeLabels, StockingTypeLabels.Length))
                {
                    item.Type = (Configuration.PurchaseType)type;
                    if (item.Type != Configuration.PurchaseType.KeepStocked)
                        item.CheckRetainerInventory = false;
                    itemsWrapper.Save();
                }

                if (item.Type == Configuration.PurchaseType.KeepStocked && item.ItemId != ItemIds.Venture)
                {
                    bool checkRetainerInventory = item.CheckRetainerInventory;
                    if (ImGui.Checkbox("Check Retainer Inventory for items (requires AllaganTools)",
                            ref checkRetainerInventory))
                    {
                        item.CheckRetainerInventory = checkRetainerInventory;
                        itemsWrapper.Save();
                    }
                }

                ImGui.EndPopup();
            }

            ImGui.SameLine(0, 3);
            ImGui.BeginDisabled(!enabled);
            int comboValueIndex = comboValues.FindIndex(x => x.Item.ItemId == item.ItemId);
            if (comboValueIndex < 0)
            {
                item.ItemId = 0;
                item.Limit = 0;
                itemsWrapper.Save();

                comboValueIndex = 0;
            }

            var comboItem = comboValues[comboValueIndex];
            IDalamudTextureWrap? icon = _iconCache.GetIcon(comboItem.Item.IconId);
            if (icon != null)
            {
                ImGui.Image(icon.ImGuiHandle, new Vector2(ImGui.GetFrameHeight()));
                ImGui.SameLine(0, 3);
            }

            indentX = ImGui.GetCursorPosX() - indentX;

            if (ImGui.Combo("", ref comboValueIndex,
                    comboValues.Select(x => item.CheckRetainerInventory ? x.NameWithRetainers : x.NameWithoutRetainers)
                        .ToArray(), comboValues.Count))
            {
                comboItem = comboValues[comboValueIndex];
                item.ItemId = comboItem.Item.ItemId;
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
                ImGui.Indent(indentX);
                if (comboValueIndex > 0)
                {
                    ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 130);
                    int limit = Math.Min(item.Limit, (int)comboItem.Item.InventoryLimit);
                    int stepSize = comboItem.Item.StackSize < 99 ? 1 : 50;
                    string label = item.Type == Configuration.PurchaseType.KeepStocked
                        ? "Maximum items to buy"
                        : "Remaining items to buy";
                    if (ImGui.InputInt(label, ref limit, stepSize, stepSize * 10))
                    {
                        item.Limit = Math.Min(Math.Max(0, limit), (int)comboItem.Item.InventoryLimit);
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
                    if (!comboItem.Item.GrandCompanies.Contains(grandCompany))
                    {
                        ImGui.TextColored(ImGuiColors.DalamudRed,
                            "This item will be skipped, as you are in the wrong Grand Company.");
                    }
                    else if (comboItem.Item.RequiredRank > _plugin.GetGrandCompanyRank())
                    {
                        ImGui.TextColored(ImGuiColors.DalamudRed,
                            "This item will be skipped, your rank isn't high enough to buy it.");
                    }
                }

                ImGui.Unindent(indentX);
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

            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Cog, "Configure available Items"))
                _configWindow.IsOpen = true;
        }
    }

    private unsafe uint CalculateEffectiveLimit(uint itemId, uint limit, uint stackSize, uint inventoryLimit)
    {
        if (itemId == ItemIds.Venture)
            return Math.Min(limit, inventoryLimit);
        else
        {
            uint slotsThatCanBeUsed = 0;
            InventoryManager* inventoryManager = InventoryManager.Instance();
            foreach (var inventoryType in InventoryTypes)
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

            return Math.Min(Math.Min(limit, slotsThatCanBeUsed * stackSize), inventoryLimit);
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
