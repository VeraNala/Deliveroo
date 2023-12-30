using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private void InteractWithPersonnelOfficer(GameObject personnelOfficer, GameObject quartermaster)
    {
        if (_targetManager.Target == quartermaster)
            return;

        InteractWithTarget(personnelOfficer);
        CurrentStage = Stage.OpenGcSupply;
    }

    private unsafe void SelectExpertDeliveryTab()
    {
        var agentInterface = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanySupply);
        if (agentInterface != null && agentInterface->IsAgentActive())
        {
            var addonId = agentInterface->GetAddonID();
            if (addonId == 0)
                return;

            AtkUnitBase* addon = LAddon.GetAddonById(addonId);
            if (addon == null || !LAddon.IsAddonReady(addon))
                return;

            // if using haseltweaks, this *can* be the default
            var addonGc = (AddonGrandCompanySupplyList*)addon;
            if (addonGc->SelectedTab == 2)
            {
                _pluginLog.Information("Tab already selected, probably due to haseltweaks");
                ResetTurnInErrorHandling();
                CurrentStage = Stage.SelectItemToTurnIn;
                return;
            }

            _pluginLog.Information("Switching to expert deliveries");
            var selectExpertDeliveryTab = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 0 },
                new() { Type = ValueType.Int, Int = 2 },
                new() { Type = 0, Int = 0 }
            };
            addon->FireCallback(3, selectExpertDeliveryTab);
            ResetTurnInErrorHandling();
            CurrentStage = Stage.SelectItemToTurnIn;
        }
    }

    private void ResetTurnInErrorHandling(int listSize = int.MaxValue)
    {
        _pluginLog.Verbose("Resetting error handling state");
        _lastTurnInListSize = listSize;
        _turnInErrors = 0;
    }

    private unsafe void SelectItemToTurnIn()
    {
        var agentInterface = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanySupply);
        if (agentInterface != null && agentInterface->IsAgentActive())
        {
            var addonId = agentInterface->GetAddonID();
            if (addonId == 0)
                return;

            AtkUnitBase* addon = LAddon.GetAddonById(addonId);
            if (addon == null || !LAddon.IsAddonReady(addon))
                return;

            var addonGc = (AddonGrandCompanySupplyList*)addon;
            if (addonGc->ExpertDeliveryList == null || !addonGc->ExpertDeliveryList->AtkComponentBase.OwnerNode->AtkResNode.IsVisible)
                return;

            if (addonGc->SelectedTab != 2)
            {
                _turnInWindow.Error = "Wrong tab selected";
                return;
            }

            ItemFilterType configuredFilter = ResolveSelectedSupplyFilter();
            if (addonGc->SelectedFilter == 0 || addonGc->SelectedFilter != (int)configuredFilter)
            {
                _turnInWindow.Error =
                    $"Wrong filter selected (expected {configuredFilter}, but is {(ItemFilterType)addonGc->SelectedFilter})";
                return;
            }

            int currentListSize = addonGc->ExpertDeliveryList->ListLength;
            if (addonGc->ListEmptyTextNode->AtkResNode.IsVisible || currentListSize == 0)
            {
                _pluginLog.Information($"No items to turn in ({addonGc->ListEmptyTextNode->AtkResNode.IsVisible}, {currentListSize})");
                CurrentStage = Stage.CloseGcSupplyThenStop;
                addon->FireCallbackInt(-1);
                return;
            }

            // Fallback: Two successive calls to SelectItemToTurnIn should *not* have lists of the same length, or
            // something is wrong.
            if (_turnInErrors > 10)
            {
                _turnInWindow.Error = "Unable to refresh item list";
                return;
            }

            if (currentListSize >= _lastTurnInListSize)
            {
                _turnInErrors++;
                _pluginLog.Information($"Trying to refresh expert delivery list manually ({_turnInErrors}, old list size = {_lastTurnInListSize}, new list size = {currentListSize})...");
                addon->FireCallbackInt(2);

                _continueAt = DateTime.Now.AddSeconds(0.1);
                return;
            }

            ResetTurnInErrorHandling(currentListSize);

            var agent = (AgentGrandCompanySupply*)agentInterface;
            List<TurnInItem> items = BuildTurnInList(agent);
            if (items.Count == 0)
            {
                // probably shouldn't happen with the previous node visibility check
                CurrentStage = Stage.CloseGcSupplyThenStop;
                addon->FireCallbackInt(-1);
                return;
            }

            // TODO The way the items are handled above, we don't actually know if items[0] is the first visible item
            // in the list, it is "only" the highest-value item to turn in.
            //
            // For example, if you have
            //   - Trojan Ring, SealsWithoutBonus = 1887, part of a gear set
            //   - Radiant Battleaxe, SealsWithoutBonus = 1879, not part of a gear set
            // then this algorithm will ensure that you have enough space for the highest-value item (trojan ring), even
            // though it turn in the Radiant Battleaxe.
            //
            // Alternatively, and probably easier:
            //   - look up how many seals the first item gets
            //   - find *any* item in the list with that seal count (and possibly matching the name) to determine the
            //     seals with bonus + whether it exists
            //   - use that item instead of items[0] here
            //
            // However, since this never over-caps seals, this isn't a very high priority.
            // ---------------------------------------------------------------------------------------------------------
            // TODO If we ever manage to obtain a mapping name to itemId here, we can try and exclude e.g. Red Onion
            // Helms from being turned in.
            if (GetCurrentSealCount() + items[0].SealsWithBonus > GetSealCap())
            {
                CurrentStage = Stage.CloseGcSupply;
                addon->FireCallbackInt(-1);
                return;
            }

            var selectFirstItem = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 1 },
                new() { Type = ValueType.Int, Int = 0 /* position within list */ },
                new() { Type = 0, Int = 0 }
            };
            addon->FireCallback(3, selectFirstItem);
            CurrentStage = Stage.TurnInSelected;
        }
    }

    private unsafe void TurnInSelectedItem()
    {
        if (_gameGui.TryGetAddonByName<AddonGrandCompanySupplyReward>("GrandCompanySupplyReward",
                out var addonSupplyReward) && LAddon.IsAddonReady(&addonSupplyReward->AtkUnitBase))
        {
            _pluginLog.Information($"Turning in '{addonSupplyReward->AtkUnitBase.AtkValues[4].ReadAtkString()}'");

            addonSupplyReward->AtkUnitBase.FireCallbackInt(0);
            _continueAt = DateTime.Now.AddSeconds(0.58);
            CurrentStage = Stage.FinalizeTurnIn;
        }
    }

    private unsafe void FinalizeTurnInItem()
    {
        if (_gameGui.TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList",
                out var addonSupplyList) && LAddon.IsAddonReady(&addonSupplyList->AtkUnitBase))
        {
            var updateFilter = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 5 },
                new() { Type = ValueType.Int, Int = (int)ResolveSelectedSupplyFilter() },
                new() { Type = 0, Int = 0 }
            };
            addonSupplyList->AtkUnitBase.FireCallback(3, updateFilter);
            CurrentStage = Stage.SelectItemToTurnIn;
        }
    }

    private ItemFilterType ResolveSelectedSupplyFilter()
    {
        if (CharacterConfiguration is { UseHideArmouryChestItemsFilter: true })
            return ItemFilterType.HideArmouryChestItems;

        return ItemFilterType.HideGearSetItems;
    }
}
