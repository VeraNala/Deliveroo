using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

    private readonly Dictionary<uint, GcRewardItem> _itemLookup;
    private uint _dragDropSource = 0;

    public ConfigWindow(DalamudPluginInterface pluginInterface, DeliverooPlugin plugin, Configuration configuration,
        GcRewardsCache gcRewardsCache)
        : base("Deliveroo - Configuration###DeliverooConfig")
    {
        _pluginInterface = pluginInterface;
        _plugin = plugin;
        _configuration = configuration;
        _gcRewardsCache = gcRewardsCache;

        _itemLookup = _gcRewardsCache.Rewards.Values
            .SelectMany(x => x)
            .Distinct()
            .ToDictionary(x => x.ItemId, x => x);

        Size = new Vector2(400, 300);
        SizeCondition = ImGuiCond.Appearing;
    }

    public override unsafe void Draw()
    {
        ImGui.Text("Items available for Auto-Buy:");
        uint? itemToRemove = null;
        uint? itemToAdd = null;
        int indexToAdd = 0;

        if (_configuration.ItemsAvailableForPurchase.Count == 0)
        {
            _configuration.ItemsAvailableForPurchase.Add(ItemIds.Venture);
            _pluginInterface.SavePluginConfig(_configuration);
        }

        if (ImGui.BeginChild("Items", new Vector2(-1, -30), true, ImGuiWindowFlags.NoSavedSettings))
        {
            for (int i = 0; i < _configuration.ItemsAvailableForPurchase.Count; ++i)
            {
                uint itemId = _configuration.ItemsAvailableForPurchase[i];
                ImGui.PushID($"###Item{i}");
                ImGui.BeginDisabled(_configuration.ItemsAvailableForPurchase.Count == 1 && itemId == ItemIds.Venture);

                ImGui.Selectable(_itemLookup[itemId].Name);

                if (ImGui.BeginDragDropSource())
                {
                    ImGui.SetDragDropPayload("DeliverooDragDrop", nint.Zero, 0);
                    _dragDropSource = itemId;

                    ImGui.EndDragDropSource();
                }

                if (ImGui.BeginDragDropTarget() &&
                    _dragDropSource > 0 &&
                    ImGui.AcceptDragDropPayload("DeliverooDragDrop").NativePtr != null)
                {
                    itemToAdd = _dragDropSource;
                    indexToAdd = i;

                    ImGui.EndDragDropTarget();
                    _dragDropSource = 0;
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
            _pluginInterface.SavePluginConfig(_configuration);
        }

        if (itemToAdd != null)
        {
            _configuration.ItemsAvailableForPurchase.Remove(itemToAdd.Value);
            _configuration.ItemsAvailableForPurchase.Insert(indexToAdd, itemToAdd.Value);
            _pluginInterface.SavePluginConfig(_configuration);
        }

        if (_plugin.GetGrandCompany() != GrandCompany.None)
        {
            List<(uint ItemId, string Name)> comboValues = _gcRewardsCache.Rewards[_plugin.GetGrandCompany()]
                .Where(x => x.SubCategory == RewardSubCategory.Materials || x.SubCategory == RewardSubCategory.Materiel)
                .Where(x => x.StackSize > 1)
                .Where(x => !_configuration.ItemsAvailableForPurchase.Contains(x.ItemId))
                .Select(x => (x.ItemId, x.Name))
                .OrderBy(x => x.Name)
                .ThenBy(x => x.GetHashCode())
                .ToList();
            comboValues.Insert(0, (0, ""));

            int currentItem = 0;
            if (ImGui.Combo("Add Item", ref currentItem, comboValues.Select(x => x.Name).ToArray(), comboValues.Count))
            {
                _configuration.ItemsAvailableForPurchase.Add(comboValues[currentItem].ItemId);
                _pluginInterface.SavePluginConfig(_configuration);
            }
        }
        else
        {
            int currentItem = 0;
            ImGui.Combo("Add Item", ref currentItem, new string[] { "(Not part of a GC)" }, 1);
        }

        ImGui.Unindent(30);
    }
}
