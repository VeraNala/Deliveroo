using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Plugin;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using ImGuiNET;

namespace Deliveroo.Windows;

internal sealed class ConfigWindow : Window
{
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly DeliverooPlugin _plugin;
    private readonly Configuration _configuration;
    private readonly GcRewardsCache _gcRewardsCache;
    private readonly ClientState _clientState;

    private readonly Dictionary<uint, GcRewardItem> _itemLookup;
    private uint _dragDropSource = 0;

    public ConfigWindow(DalamudPluginInterface pluginInterface, DeliverooPlugin plugin, Configuration configuration,
        GcRewardsCache gcRewardsCache, ClientState clientState)
        : base("Deliveroo - Configuration###DeliverooConfig")
    {
        _pluginInterface = pluginInterface;
        _plugin = plugin;
        _configuration = configuration;
        _gcRewardsCache = gcRewardsCache;
        _clientState = clientState;

        _itemLookup = _gcRewardsCache.Rewards.Values
            .SelectMany(x => x)
            .Distinct()
            .ToDictionary(x => x.ItemId, x => x);

        Size = new Vector2(420, 300);
        SizeCondition = ImGuiCond.Appearing;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 300),
            MaximumSize = new Vector2(9999, 9999),
        };
    }

    public override void Draw()
    {
        if (_configuration.ItemsAvailableForPurchase.Count == 0)
        {
            _configuration.ItemsAvailableForPurchase.Add(ItemIds.Venture);
            Save();
        }

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
            if (ImGui.BeginChild("Items", new Vector2(-1, -30), true, ImGuiWindowFlags.NoSavedSettings))
            {
                for (int i = 0; i < _configuration.ItemsAvailableForPurchase.Count; ++i)
                {
                    uint itemId = _configuration.ItemsAvailableForPurchase[i];
                    ImGui.PushID($"###Item{i}");
                    ImGui.BeginDisabled(
                        _configuration.ItemsAvailableForPurchase.Count == 1 && itemId == ItemIds.Venture);

                    ImGui.Selectable(_itemLookup[itemId].Name);

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

            if (_plugin.GetGrandCompany() != GrandCompany.None)
            {
                List<(uint ItemId, string Name)> comboValues = _gcRewardsCache.Rewards[_plugin.GetGrandCompany()]
                    .Where(x => x.SubCategory == RewardSubCategory.Materials ||
                                x.SubCategory == RewardSubCategory.Materiel)
                    .Where(x => x.StackSize > 1)
                    .Where(x => !_configuration.ItemsAvailableForPurchase.Contains(x.ItemId))
                    .Select(x => (x.ItemId, x.Name))
                    .OrderBy(x => x.Name)
                    .ThenBy(x => x.GetHashCode())
                    .ToList();
                comboValues.Insert(0, (0, ""));

                int currentItem = 0;
                if (ImGui.Combo("Add Item", ref currentItem, comboValues.Select(x => x.Name).ToArray(),
                        comboValues.Count))
                {
                    _configuration.ItemsAvailableForPurchase.Add(comboValues[currentItem].ItemId);
                    Save();
                }
            }
            else
            {
                int currentItem = 0;
                ImGui.Combo("Add Item", ref currentItem, new[] { "(Not part of a GC)" }, 1);
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
                    bool useHideArmouryChestItemsFilter = charConfiguration.UseHideArmouryChestItemsFilter;
                    if (ImGui.Checkbox("Use 'Hide Armoury Chest Items' filter", ref useHideArmouryChestItemsFilter))
                    {
                        charConfiguration.UseHideArmouryChestItemsFilter = useHideArmouryChestItemsFilter;
                        charConfiguration.Save(_pluginInterface);
                    }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("The default filter for all characters is 'Hide Gear Set Items', but you may want to override this to hide all Armoury Chest items (regardless of whether they're part of a gear set) e.g. for your main character.");

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
                        PluginLog.Information(
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
            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 100);
            int reservedSealCount = _configuration.ReservedSealCount;
            if (ImGui.InputInt("Minimum Seals to keep (e.g. for Squadron Missions)", ref reservedSealCount, 1000))
            {
                _configuration.ReservedSealCount = Math.Max(0, Math.Min(90_000, reservedSealCount));
                Save();
            }

            ImGui.EndTabItem();
        }
    }


    private void Save() => _pluginInterface.SavePluginConfig(_configuration);
}
