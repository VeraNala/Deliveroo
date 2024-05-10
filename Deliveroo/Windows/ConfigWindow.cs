using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Internal;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Deliveroo.GameData;
using ImGuiNET;
using LLib;
using LLib.ImGui;

namespace Deliveroo.Windows;

internal sealed class ConfigWindow : LWindow, IPersistableWindowConfig
{
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly DeliverooPlugin _plugin;
    private readonly Configuration _configuration;
    private readonly GcRewardsCache _gcRewardsCache;
    private readonly IClientState _clientState;
    private readonly IPluginLog _pluginLog;
    private readonly IconCache _iconCache;
    private readonly GameFunctions _gameFunctions;

    private readonly IReadOnlyDictionary<uint, GcRewardItem> _itemLookup;
    private string _searchString = string.Empty;
    private uint _dragDropSource;

    public ConfigWindow(DalamudPluginInterface pluginInterface, DeliverooPlugin plugin, Configuration configuration,
        GcRewardsCache gcRewardsCache, IClientState clientState, IPluginLog pluginLog, IconCache iconCache,
        GameFunctions gameFunctions)
        : base("Deliveroo - Configuration###DeliverooConfig")
    {
        _pluginInterface = pluginInterface;
        _plugin = plugin;
        _configuration = configuration;
        _gcRewardsCache = gcRewardsCache;
        _clientState = clientState;
        _pluginLog = pluginLog;
        _iconCache = iconCache;
        _gameFunctions = gameFunctions;

        _itemLookup = _gcRewardsCache.RewardLookup;

        Size = new Vector2(440, 300);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 300),
            MaximumSize = new Vector2(9999, 9999),
        };
    }

    public WindowConfig WindowConfig => _configuration.ConfigWindowConfig;

    public override void Draw()
    {
        if (_configuration.AddVentureIfNoItemToPurchaseSelected())
            Save();

        if (ImGui.BeginTabBar("DeliverooConfigTabs"))
        {
            DrawBuyList();
            DrawCharacterSpecificSettings();
            DrawAdditionalSettings();

            ImGui.EndTabBar();
        }
    }

    private unsafe void DrawBuyList()
    {
        if (ImGui.BeginTabItem("Items for Auto-Buy"))
        {
            uint? itemToRemove = null;
            uint? itemToAdd = null;
            int indexToAdd = 0;
            if (ImGui.BeginChild("Items", new Vector2(-1, -ImGui.GetFrameHeightWithSpacing()), true,
                    ImGuiWindowFlags.NoSavedSettings))
            {
                for (int i = 0; i < _configuration.ItemsAvailableForPurchase.Count; ++i)
                {
                    uint itemId = _configuration.ItemsAvailableForPurchase[i];
                    ImGui.PushID($"###Item{i}");
                    ImGui.BeginDisabled(
                        _configuration.ItemsAvailableForPurchase.Count == 1 && itemId == ItemIds.Venture);

                    var item = _itemLookup[itemId];
                    IDalamudTextureWrap? icon = _iconCache.GetIcon(item.IconId);
                    Vector2 pos = ImGui.GetCursorPos();
                    Vector2 iconSize = new Vector2(ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y);
                    if (icon != null)
                    {
                        ImGui.SetCursorPos(pos + new Vector2(iconSize.X + ImGui.GetStyle().FramePadding.X,
                            ImGui.GetStyle().ItemSpacing.Y / 2));
                    }

                    ImGui.Selectable($"{item.Name}{(item.Limited ? $" {SeIconChar.Hyadelyn.ToIconString()}" : "")}",
                        false, ImGuiSelectableFlags.SpanAllColumns);

                    if (icon != null)
                    {
                        ImGui.SameLine(0, 0);
                        ImGui.SetCursorPos(pos);
                        ImGui.Image(icon.ImGuiHandle, iconSize);
                    }

                    if (ImGui.BeginDragDropSource())
                    {
                        ImGui.SetDragDropPayload("DeliverooDragDrop", nint.Zero, 0);
                        _dragDropSource = itemId;

                        ImGui.EndDragDropSource();
                    }

                    if (ImGui.BeginDragDropTarget())
                    {
                        if (_dragDropSource > 0 &&
                            ImGui.AcceptDragDropPayload("DeliverooDragDrop").NativePtr != null)
                        {
                            itemToAdd = _dragDropSource;
                            indexToAdd = i;

                            _dragDropSource = 0;
                        }

                        ImGui.EndDragDropTarget();
                    }

                    ImGui.OpenPopupOnItemClick($"###ctx{i}", ImGuiPopupFlags.MouseButtonRight);
                    if (ImGui.BeginPopup($"###ctx{i}"))
                    {
                        if (ImGui.Selectable($"Remove {_itemLookup[itemId].Name}"))
                            itemToRemove = itemId;

                        ImGui.EndPopup();
                    }

                    ImGui.EndDisabled();
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();

            if (itemToRemove != null)
            {
                _configuration.ItemsAvailableForPurchase.Remove(itemToRemove.Value);
                Save();
            }

            if (itemToAdd != null)
            {
                _configuration.ItemsAvailableForPurchase.Remove(itemToAdd.Value);
                _configuration.ItemsAvailableForPurchase.Insert(indexToAdd, itemToAdd.Value);
                Save();
            }

            List<(uint ItemId, string Name, ushort IconId, bool Limited)> comboValues = _gcRewardsCache.Rewards
                .Where(x => x.SubCategory is RewardSubCategory.Materials or RewardSubCategory.Materiel)
                .Where(x => !_configuration.ItemsAvailableForPurchase.Contains(x.ItemId))
                .Select(x => (x.ItemId, x.Name, x.IconId, x.Limited))
                .OrderBy(x => x.Name)
                .ThenBy(x => x.GetHashCode())
                .ToList();

            if (ImGui.BeginCombo($"##ItemSelection", "Add Item...", ImGuiComboFlags.HeightLarge))
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                bool addFirst = ImGui.InputTextWithHint("", "Filter...", ref _searchString, 256,
                    ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);

                foreach (var item in comboValues.Where(x =>
                             x.Name.Contains(_searchString, StringComparison.OrdinalIgnoreCase)))
                {
                    IDalamudTextureWrap? icon = _iconCache.GetIcon(item.IconId);
                    if (icon != null)
                    {
                        ImGui.Image(icon.ImGuiHandle, new Vector2(ImGui.GetFrameHeight()));
                        ImGui.SameLine();
                        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.X);
                    }

                    bool addThis =
                        ImGui.Selectable(
                            $"{item.Name}{(item.Limited ? $" {SeIconChar.Hyadelyn.ToIconString()}" : "")}##SelectVenture{item.IconId}");
                    if (addThis || addFirst)
                    {
                        _configuration.ItemsAvailableForPurchase.Add(item.ItemId);

                        if (addFirst)
                        {
                            addFirst = false;
                            ImGui.CloseCurrentPopup();
                        }

                        Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawCharacterSpecificSettings()
    {
        if (ImGui.BeginTabItem("Character Settings"))
        {
            if (_clientState is { IsLoggedIn: true, LocalContentId: > 0 })
            {
                string currentCharacterName = _clientState.LocalPlayer!.Name.ToString();
                string currentWorldName = _clientState.LocalPlayer.HomeWorld.GameData!.Name.ToString();
                ImGui.Text($"Current Character: {currentCharacterName} @ {currentWorldName}");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var charConfiguration = _plugin.CharacterConfiguration;
                if (charConfiguration != null)
                {
                    bool disableForCharacter = charConfiguration.DisableForCharacter;
                    if (ImGui.Checkbox("Disable plugin for this character", ref disableForCharacter))
                    {
                        charConfiguration.DisableForCharacter = disableForCharacter;
                        charConfiguration.Save(_pluginInterface);
                    }

                    ImGui.BeginDisabled(charConfiguration.DisableForCharacter);

                    bool overrideItemsToPurchase = charConfiguration.OverrideItemsToPurchase;
                    if (ImGui.Checkbox("Use custom purchase list for this character", ref overrideItemsToPurchase))
                    {
                        charConfiguration.OverrideItemsToPurchase = overrideItemsToPurchase;
                        charConfiguration.Save(_pluginInterface);
                    }

                    if (charConfiguration.ItemsToPurchase.Count > 1 ||
                        (charConfiguration.ItemsToPurchase.Count == 1 &&
                         charConfiguration.ItemsToPurchase[0].ItemId != GcRewardItem.None.ItemId))
                    {
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "Clear"))
                        {
                            charConfiguration.ItemsToPurchase.Clear();
                            charConfiguration.Save(_pluginInterface);
                        }
                    }

                    bool useHideArmouryChestItemsFilter = charConfiguration.UseHideArmouryChestItemsFilter;
                    if (ImGui.Checkbox("Use 'Hide Armoury Chest Items' filter", ref useHideArmouryChestItemsFilter))
                    {
                        charConfiguration.UseHideArmouryChestItemsFilter = useHideArmouryChestItemsFilter;
                        charConfiguration.Save(_pluginInterface);
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(
                        "The default filter for all characters is 'Hide Gear Set Items', but you may want to override this to hide all Armoury Chest items (regardless of whether they're part of a gear set) e.g. for your main character.");

                    bool ignoreMinimumSealsToKeep = charConfiguration.IgnoreMinimumSealsToKeep;
                    if (ImGui.Checkbox("Ignore 'Minimum Seals to keep' setting", ref ignoreMinimumSealsToKeep))
                    {
                        charConfiguration.IgnoreMinimumSealsToKeep = ignoreMinimumSealsToKeep;
                        charConfiguration.Save(_pluginInterface);
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(
                        "When enabled, all GC seals will be spent. This is effectively the same as setting 'Minimum Seals to keep' to 0.");

                    ImGui.EndDisabled();
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl);
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PersonCircleMinus,
                            "Remove character-specific settings"))
                    {
                        charConfiguration.Delete(_pluginInterface);
                        _plugin.CharacterConfiguration = null;
                    }

                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ImGui.GetIO().KeyCtrl)
                        ImGui.SetTooltip(
                            $"Hold CTRL to remove the configuration for {currentCharacterName} (non-reversible).");
                }
                else
                {
                    // no settings
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PersonCirclePlus,
                            "Enable character-specific settings"))
                    {
                        _plugin.CharacterConfiguration = new()
                        {
                            LocalContentId = _clientState.LocalContentId,
                            CachedPlayerName = currentCharacterName,
                            CachedWorldName = currentWorldName,
                        };
                        _plugin.CharacterConfiguration.Save(_pluginInterface);
                        _pluginLog.Information(
                            $"Created character-specific configuration for {_clientState.LocalContentId}");
                    }
                }
            }
            else
            {
                ImGui.Text("You are not currently logged in.");
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawAdditionalSettings()
    {
        if (ImGui.BeginTabItem("Additional Settings"))
        {
            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            int reservedSealCount = _configuration.ReservedSealCount;
            if (ImGui.InputInt("Minimum Seals to keep (e.g. for Squadron Missions)", ref reservedSealCount, 1000))
            {
                _configuration.ReservedSealCount =
                    Math.Max(0, Math.Min((int)_gameFunctions.MaxSealCap, reservedSealCount));
                Save();
            }

            ImGui.BeginDisabled(reservedSealCount <= 0);
            bool reserveDifferentSealCountAtMaxRank = _configuration.ReserveDifferentSealCountAtMaxRank;
            if (ImGui.Checkbox("Use a different amount at max rank", ref reserveDifferentSealCountAtMaxRank))
            {
                _configuration.ReserveDifferentSealCountAtMaxRank = reserveDifferentSealCountAtMaxRank;
                Save();
            }

            if (reserveDifferentSealCountAtMaxRank)
            {
                float indentSize = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
                ImGui.Indent(indentSize);
                ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 100);
                int reservedSealCountAtMaxRank = _configuration.ReservedSealCountAtMaxRank;
                if (ImGui.InputInt("Minimum seals to keep at max rank", ref reservedSealCountAtMaxRank))
                {
                    _configuration.ReservedSealCountAtMaxRank = Math.Max(0,
                        Math.Min((int)_gameFunctions.MaxSealCap, reservedSealCountAtMaxRank));
                    Save();
                }

                ImGui.Unindent(indentSize);
            }

            ImGui.EndDisabled();

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            var xivChatTypes = Enum.GetValues<XivChatType>()
                .Where(x => x != XivChatType.StandardEmote)
                .ToArray();
            var selectedChatType = Array.IndexOf(xivChatTypes, _configuration.ChatType);
            string[] chatTypeNames = xivChatTypes
                .Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString())
                .ToArray();
            if (ImGui.Combo("Chat channel for status updates", ref selectedChatType, chatTypeNames,
                    chatTypeNames.Length))
            {
                _configuration.ChatType = xivChatTypes[selectedChatType];
                Save();
            }

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            List<(int Rank, string Name)> rankUpComboValues = Enumerable.Range(1, 30)
                .Select(x => x == 1 ? (0, "---") : (x, $"Rank {x}"))
                .ToList();
            int pauseAtRank = Math.Max(rankUpComboValues.FindIndex(x => x.Rank == _configuration.PauseAtRank), 0);
            if (ImGui.Combo("Pause when reaching selected FC Rank", ref pauseAtRank,
                    rankUpComboValues.Select(x => x.Name).ToArray(),
                    rankUpComboValues.Count))
            {
                _configuration.PauseAtRank = rankUpComboValues[pauseAtRank].Rank;
                Save();
            }

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            string[] behaviorOnOtherWorldNames = { "---", "Show Warning", "Disable Turn-In" };
            int behaviorOnOtherWorld = (int)_configuration.BehaviorOnOtherWorld;
            if (ImGui.Combo("Behavior when not on Home World", ref behaviorOnOtherWorld,
                    behaviorOnOtherWorldNames, behaviorOnOtherWorldNames.Length))
            {
                _configuration.BehaviorOnOtherWorld = (Configuration.EBehaviorOnOtherWorld)behaviorOnOtherWorld;
                Save();
            }

            ImGui.Separator();

            bool disableFrameLimiter = _configuration.DisableFrameLimiter;
            if (ImGui.Checkbox("Disable the game setting 'Limit frame rate when client is inactive'",
                    ref disableFrameLimiter))
            {
                _configuration.DisableFrameLimiter = disableFrameLimiter;
                Save();
            }

            ImGui.Indent();
            ImGui.BeginDisabled(!disableFrameLimiter);
            bool uncapFrameRate = _configuration.UncapFrameRate;
            if (ImGui.Checkbox("Set frame rate to uncapped", ref uncapFrameRate))
            {
                _configuration.UncapFrameRate = uncapFrameRate;
                Save();
            }

            ImGui.EndDisabled();
            ImGui.Unindent();
            ImGui.EndTabItem();
        }
    }


    private void Save() => _pluginInterface.SavePluginConfig(_configuration);

    public void SaveWindowConfig() => Save();
}
