using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Dalamud.Bindings.ImGui;
using LLib;
using LLib.ImGui;

namespace Deliveroo.Windows;

internal sealed class TurnInWindow : LWindow, IPersistableWindowConfig<Configuration.MinimizableWindowConfig>
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
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly Configuration _configuration;
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly GcRewardsCache _gcRewardsCache;
    private readonly ConfigWindow _configWindow;
    private readonly IconCache _iconCache;
    private readonly IKeyState _keyState;
    private readonly GameFunctions _gameFunctions;
    private readonly TitleBarButton _minimizeButton;

    private bool _state;
    private Guid? _draggedItem;

    public TurnInWindow(DeliverooPlugin plugin, IDalamudPluginInterface pluginInterface, Configuration configuration,
        ICondition condition, IClientState clientState, GcRewardsCache gcRewardsCache, ConfigWindow configWindow,
        IconCache iconCache, IKeyState keyState, GameFunctions gameFunctions)
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
        _keyState = keyState;
        _gameFunctions = gameFunctions;

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

        _minimizeButton = new TitleBarButton
        {
            Icon = IsMinimized ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.Minus,
            Priority = int.MinValue,
            IconOffset = new Vector2(1.5f, 1),
            Click = _ =>
            {
                IsMinimized = !IsMinimized;
                _minimizeButton!.Icon = IsMinimized ? FontAwesomeIcon.WindowMaximize : FontAwesomeIcon.Minus;
            },
            AvailableClickthrough = true,
        };
        TitleBarButtons.Insert(0, _minimizeButton);

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

    public Configuration.MinimizableWindowConfig WindowConfig => _configuration.TurnInWindowConfig;

    private bool IsMinimized
    {
        get => WindowConfig.IsMinimized;
        set
        {
            WindowConfig.IsMinimized = value;
            SaveWindowConfig();
        }
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
        _clientState.LocalPlayer.HomeWorld.RowId == _clientState.LocalPlayer.CurrentWorld.RowId;

    private IItemsToPurchase ItemsWrapper => UseCharacterSpecificItemsToPurchase
        ? new CharacterSpecificItemsToPurchase(_plugin.CharacterConfiguration!, _pluginInterface)
        : new GlobalItemsToPurchase(_configuration, _pluginInterface);

    public List<PurchaseItemRequest> SelectedItems
    {
        get
        {
            GrandCompany grandCompany = _gameFunctions.GetGrandCompany();
            if (grandCompany == GrandCompany.None)
                return new List<PurchaseItemRequest>();

            var rank = _gameFunctions.GetGrandCompanyRank();
            return ItemsWrapper.GetItemsToPurchase()
                .Where(x => x.ItemId != GcRewardItem.None.ItemId)
                .Where(x => x.Enabled)
                .Where(x => x.Type == Configuration.PurchaseType.KeepStocked || x.Limit > 0)
                .Select(x => new { Item = x, Reward = _gcRewardsCache.GetReward(x.ItemId) })
                .Where(x => x.Reward.GrandCompanies.Contains(grandCompany))
                .Where(x => x.Reward.RequiredRank <= rank)
                .Select(x =>
                {
                    var purchaseOption = _configuration.ItemsAvailableToPurchase.Where(y => y.SameQuantityForAllLists)
                        .FirstOrDefault(y => y.ItemId == x.Item.ItemId);
                    int limit = purchaseOption?.GlobalLimit ?? x.Item.Limit;
                    var request = new PurchaseItemRequest
                    {
                        ItemId = x.Item.ItemId,
                        Name = x.Reward.Name,
                        EffectiveLimit = CalculateEffectiveLimit(
                            x.Item.ItemId,
                            limit <= 0 ? uint.MaxValue : (uint)limit,
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

    public override unsafe void DrawContent()
    {
        GrandCompany grandCompany = _gameFunctions.GetGrandCompany();
        if (grandCompany == GrandCompany.None)
        {
            // not sure we should ever get here
            State = false;
            return;
        }

        if (_gameFunctions.GetGrandCompanyRank() < 6)
        {
            State = false;
            ImGui.TextColored(ImGuiColors.DalamudRed, "You do not have the required rank for Expert Delivery.");

            DrawNextRankPrequesites();
            return;
        }
        else if (_configuration.BehaviorOnOtherWorld == Configuration.EBehaviorOnOtherWorld.DisableTurnIn &&
                 !IsOnHomeWorld)
        {
            State = false;
            ImGui.TextColored(ImGuiColors.DalamudRed, "Turn-in disabled, you are not on your home world.");
            return;
        }

        bool state = State;
        if (ImGui.Checkbox("Handle GC turn ins/exchange automatically", ref state))
        {
            State = state;
        }

        float indentSize = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        if (!string.IsNullOrEmpty(Error))
        {
            using (ImRaii.PushIndent(indentSize))
                ImGui.TextColored(ImGuiColors.DalamudRed, Error);
        }
        else
        {
            using (ImRaii.PushIndent(indentSize))
            {
                if (_configuration.BehaviorOnOtherWorld == Configuration.EBehaviorOnOtherWorld.Warning &&
                    !IsOnHomeWorld)
                {
                    ImGui.TextColored(ImGuiColors.DalamudRed,
                        "You are not on your home world and will not earn FC points.");
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
                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Bolt,
                                "Use Priority Seal Allowance (15%)"))
                        {
                            agentInventoryContext->UseItem(ItemIds.PrioritySealAllowance);
                        }

                        ImGui.EndDisabled();
                    }
                }
            }

            if (!IsMinimized)
            {
                ImGui.Separator();
                using (ImRaii.Disabled(state))
                    DrawItemsToBuy(grandCompany);
            }
        }

        if (_configuration.QuickTurnInKey != VirtualKey.NO_KEY)
        {
            var key = _configuration.QuickTurnInKey switch
            {
                VirtualKey.MENU => "ALT",
                VirtualKey.SHIFT => "SHIFT",
                VirtualKey.CONTROL => "CTRL",
                _ => _configuration.QuickTurnInKey.ToString()
            };
            if (!State && _keyState[_configuration.QuickTurnInKey])
            {
                ImGui.Separator();
                ImGui.TextColored(ImGuiColors.HealerGreen, "Click an item to turn it in without confirmation");
            }
            else if (!IsMinimized)
            {
                ImGui.Separator();
                ImGui.Text($"Hold '{key}' when clicking an item to turn it in without confirmation.");
            }
        }

        if (!IsMinimized)
        {
            ImGui.Separator();
            ImGui.Text($"Debug (State): {_plugin.CurrentStage}");
        }
    }

    private unsafe void DrawNextRankPrequesites()
    {
        string? rankName = _gameFunctions.GetNextGrandCompanyRankName();
        if (rankName != null)
        {
            int currentSeals = _gameFunctions.GetCurrentSealCount();
            uint requiredSeals = _gameFunctions.GetSealsRequiredForNextRank();

            int currentHuntingLog =
                MonsterNoteManager.Instance()->RankData[(int)_gameFunctions.GetGrandCompany() + 7]
                    .Rank;
            byte requiredHuntingLog = _gameFunctions.GetRequiredHuntingLogForNextRank();

            bool enoughSeals = currentSeals >= requiredSeals;
            bool enoughHuntingLog = currentHuntingLog >= requiredHuntingLog;

            if (enoughSeals && enoughHuntingLog)
                ImGui.TextColored(ImGuiColors.HealerGreen, $"You meet all requirements to rank up to {rankName}.");
            else
                ImGui.Text($"Ranking up to {rankName} requires:");

            ImGui.Indent();
            if (enoughSeals)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                ImGui.BulletText($"{currentSeals:N0} / {requiredSeals:N0} GC seals");
                ImGui.PopStyleColor();
            }
            else
                ImGui.BulletText($"{currentSeals:N0} / {requiredSeals:N0} GC seals");

            if (requiredHuntingLog > 0)
            {
                if (enoughHuntingLog)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.HealerGreen);
                    ImGui.BulletText($"Complete Hunting Log #{requiredHuntingLog}");
                    ImGui.PopStyleColor();
                }
                else
                    ImGui.BulletText($"Complete Hunting Log #{requiredHuntingLog}");
            }

            ImGui.Unindent();
        }
    }

    private void DrawItemsToBuy(GrandCompany grandCompany)
    {
        var itemsWrapper = ItemsWrapper;
        ImGui.Text($"Items to buy ({itemsWrapper.Name}):");

        List<(GcRewardItem Item, string NameWithoutRetainers, string NameWithRetainers)> comboValues = new()
        {
            (GcRewardItem.None, GcRewardItem.None.Name, GcRewardItem.None.Name),
        };
        foreach (Configuration.PurchaseOption purchaseOption in _configuration.ItemsAvailableToPurchase)
        {
            var gcReward = _gcRewardsCache.GetReward(purchaseOption.ItemId);
            int itemCountWithoutRetainers = _gameFunctions.GetItemCount(purchaseOption.ItemId, false);
            int itemCountWithRetainers = _gameFunctions.GetItemCount(purchaseOption.ItemId, true);
            string itemNameWithoutRetainers = gcReward.Name;
            string itemNameWithRetainers = gcReward.Name;

            if (purchaseOption.SameQuantityForAllLists)
            {
                itemNameWithoutRetainers += $" {((SeIconChar)57412).ToIconString()}";
                itemNameWithRetainers += $" {((SeIconChar)57412).ToIconString()}";
            }

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

        Configuration.PurchasePriority? itemToRemove = null;
        Configuration.PurchasePriority? itemToAdd = null;
        int indexToAdd = 0;

        float width = ImGui.GetContentRegionAvail().X;
        List<(Vector2 TopLeft, Vector2 BottomRight)> itemPositions = [];

        for (int i = 0; i < itemsWrapper.GetItemsToPurchase().Count; ++i)
        {
            DrawItemToBuy(grandCompany, i, itemsWrapper, comboValues, width, ref itemToRemove, itemPositions);
        }

        if (!ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            _draggedItem = null;
        else if (_draggedItem != null)
        {
            var items = itemsWrapper.GetItemsToPurchase().ToList();
            var draggedItem = items.Single(x => x.InternalId == _draggedItem);
            int oldIndex = items.IndexOf(draggedItem);

            var (topLeft, bottomRight) = itemPositions[oldIndex];
            ImGui.GetWindowDrawList().AddRect(topLeft, bottomRight, ImGui.GetColorU32(ImGuiColors.DalamudGrey), 3f,
                ImDrawFlags.RoundCornersAll);

            int newIndex = itemPositions.FindIndex(x => ImGui.IsMouseHoveringRect(x.TopLeft, x.BottomRight, true));
            if (newIndex >= 0 && oldIndex != newIndex)
            {
                itemToAdd = items.Single(x => x.InternalId == _draggedItem);
                indexToAdd = newIndex;
            }
        }

        if (itemToRemove != null)
        {
            itemsWrapper.Remove(itemToRemove);
            itemsWrapper.Save();
        }

        if (itemToAdd != null)
        {
            itemsWrapper.Remove(itemToAdd);
            itemsWrapper.Insert(indexToAdd, itemToAdd);
            itemsWrapper.Save();
        }

        if (_configuration.ItemsAvailableToPurchase.Any(x =>
                itemsWrapper.GetItemsToPurchase().All(y => x.ItemId != y.ItemId)))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Add Item"))
                ImGui.OpenPopup("##AddItem");

            if (ImGui.BeginPopupContextItem("##AddItem", ImGuiPopupFlags.NoOpenOverItems))
            {
                foreach (var purchaseOption in _configuration.ItemsAvailableToPurchase.Distinct())
                {
                    if (_gcRewardsCache.RewardLookup.TryGetValue(purchaseOption.ItemId, out var reward))
                    {
                        if (ImGui.MenuItem($"{reward.Name}##{purchaseOption.ItemId}"))
                        {
                            itemsWrapper.Add(new Configuration.PurchasePriority
                                { ItemId = purchaseOption.ItemId, Limit = 0 });
                            itemsWrapper.Save();
                            ImGui.CloseCurrentPopup();
                        }
                    }
                }

                ImGui.EndPopup();
            }

            ImGui.SameLine();
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Cog, "Configure available Items"))
            _configWindow.IsOpen = true;
    }

    private void DrawItemToBuy(GrandCompany grandCompany, int i, IItemsToPurchase itemsWrapper,
        List<(GcRewardItem Item, string NameWithoutRetainers, string NameWithRetainers)> comboValues, float width,
        ref Configuration.PurchasePriority? itemToRemove, List<(Vector2 TopLeft, Vector2 BottomRight)> itemPositions)
    {
        Vector2 topLeft = ImGui.GetCursorScreenPos() + new Vector2(0, -ImGui.GetStyle().ItemSpacing.Y / 2);

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
        using (var icon = _iconCache.GetIcon(comboItem.Item.IconId))
        {
            if (icon != null)
            {
                ImGui.Image(icon.Handle, new Vector2(ImGui.GetFrameHeight()));
                ImGui.SameLine(0, 3);
            }
        }

        indentX = ImGui.GetCursorPosX() - indentX;

        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.SetNextItemWidth(width -
                               ImGui.GetStyle().WindowPadding.X -
                               ImGui.CalcTextSize(FontAwesomeIcon.Circle.ToIconString()).X -
                               ImGui.CalcTextSize(FontAwesomeIcon.ArrowsUpDown.ToIconString()).X -
                               ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                               ImGui.GetStyle().FramePadding.X * 8 -
                               ImGui.GetStyle().ItemSpacing.X * 2 - 3 * 3);
        ImGui.PopFont();

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
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                           ImGui.GetStyle().WindowPadding.X -
                           ImGui.CalcTextSize(FontAwesomeIcon.ArrowsUpDown.ToIconString()).X -
                           ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                           ImGui.GetStyle().FramePadding.X * 4 -
                           ImGui.GetStyle().ItemSpacing.X);
            ImGui.PopFont();

            if (_draggedItem == item.InternalId)
            {
                ImGuiComponents.IconButton("##Move", FontAwesomeIcon.ArrowsUpDown,
                    ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.ButtonActive)));
            }
            else
                ImGuiComponents.IconButton("##Move", FontAwesomeIcon.ArrowsUpDown);

            if (_draggedItem == null && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                _draggedItem = item.InternalId;

            ImGui.SameLine();
        }
        else
        {
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.SameLine(ImGui.GetContentRegionAvail().X +
                           ImGui.GetStyle().WindowPadding.X -
                           ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X -
                           ImGui.GetStyle().FramePadding.X * 2);
            ImGui.PopFont();
        }

        if (ImGuiComponents.IconButton($"###Remove{i}", FontAwesomeIcon.Times))
            itemToRemove = item;

        if (enabled)
        {
            ImGui.Indent(indentX);
            if (comboValueIndex > 0)
            {
                var purchaseOption =
                    _configuration.ItemsAvailableToPurchase
                        .Where(x => x.SameQuantityForAllLists)
                        .FirstOrDefault(x => x.ItemId == item.ItemId);

                ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 130);
                int limit = Math.Min(purchaseOption?.GlobalLimit ?? item.Limit, (int)comboItem.Item.InventoryLimit);
                int stepSize = comboItem.Item.StackSize < 99 ? 1 : 50;
                string label = item.Type == Configuration.PurchaseType.KeepStocked
                    ? "Maximum items to buy"
                    : "Remaining items to buy";
                if (ImGui.InputInt(label, ref limit, stepSize, stepSize * 10))
                {
                    int newLimit = Math.Min(Math.Max(0, limit), (int)comboItem.Item.InventoryLimit);
                    if (purchaseOption != null)
                    {
                        purchaseOption.GlobalLimit = newLimit;
                        _pluginInterface.SavePluginConfig(_configuration);
                    }
                    else
                    {
                        item.Limit = newLimit;
                        itemsWrapper.Save();
                    }
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
                else if (comboItem.Item.RequiredRank > _gameFunctions.GetGrandCompanyRank())
                {
                    ImGui.TextColored(ImGuiColors.DalamudRed,
                        "This item will be skipped, your rank isn't high enough to buy it.");
                }
            }

            ImGui.Unindent(indentX);
        }

        ImGui.PopID();

        Vector2 bottomRight = new Vector2(topLeft.X + width,
            ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y + 2);
        itemPositions.Add((topLeft, bottomRight));
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
                    if (item == null || item->ItemId == 0 || item->ItemId == itemId)
                    {
                        slotsThatCanBeUsed++;
                    }
                }
            }

            return Math.Min(Math.Min(limit, slotsThatCanBeUsed * stackSize), inventoryLimit);
        }
    }

    public void SaveWindowConfig() => _pluginInterface.SavePluginConfig(_configuration);

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
        private readonly IDalamudPluginInterface _pluginInterface;

        public CharacterSpecificItemsToPurchase(CharacterConfiguration characterConfiguration,
            IDalamudPluginInterface pluginInterface)
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
        private readonly IDalamudPluginInterface _pluginInterface;

        public GlobalItemsToPurchase(Configuration configuration, IDalamudPluginInterface pluginInterface)
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
