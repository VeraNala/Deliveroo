using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Dalamud.Memory;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
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

    private void OpenGcSupply()
    {
        if (SelectSelectString(0))
            CurrentStage = Stage.SelectExpertDeliveryTab;
    }

    private unsafe void SelectExpertDeliveryTab()
    {
        var agentInterface = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanySupply);
        if (agentInterface != null && agentInterface->IsAgentActive())
        {
            var addonId = agentInterface->GetAddonID();
            if (addonId == 0)
                return;

            AtkUnitBase* addon = GetAddonById(addonId);
            if (addon == null || !IsAddonReady(addon))
                return;

            // if using haseltweaks, this *can* be the default
            var addonGc = (AddonGrandCompanySupplyList*)addon;
            if (addonGc->SelectedTab == 2)
            {
                PluginLog.Information("Tab already selected, probably due to haseltweaks");
                CurrentStage = Stage.SelectItemToTurnIn;
                return;
            }

            PluginLog.Information("Switching to expert deliveries");
            var selectExpertDeliveryTab = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 0 },
                new() { Type = ValueType.Int, Int = 2 },
                new() { Type = 0, Int = 0 }
            };
            addon->FireCallback(3, selectExpertDeliveryTab);
            CurrentStage = Stage.SelectItemToTurnIn;
        }
    }

    private unsafe void SelectItemToTurnIn()
    {
        var agentInterface = AgentModule.Instance()->GetAgentByInternalId(AgentId.GrandCompanySupply);
        if (agentInterface != null && agentInterface->IsAgentActive())
        {
            var addonId = agentInterface->GetAddonID();
            if (addonId == 0)
                return;

            AtkUnitBase* addon = GetAddonById(addonId);
            if (addon == null || !IsAddonReady(addon) || addon->UldManager.NodeListCount <= 20 ||
                !addon->UldManager.NodeList[5]->IsVisible)
                return;

            var addonGc = (AddonGrandCompanySupplyList*)addon;
            if (addonGc->SelectedTab != 2)
            {
                _turnInWindow.Error = "Wrong tab selected";
                return;
            }

            if (addonGc->SelectedFilter == 0 || addonGc->SelectedFilter != (int)_configuration.ItemFilter)
            {
                _turnInWindow.Error =
                    $"Wrong filter selected (expected {_configuration.ItemFilter}, but is {(Configuration.ItemFilterType)addonGc->SelectedFilter})";
                return;
            }

            var agent = (AgentGrandCompanySupply*)agentInterface;
            List<TurnInItem> items = BuildTurnInList(agent);
            if (items.Count == 0 || addon->UldManager.NodeList[20]->IsVisible)
            {
                CurrentStage = Stage.CloseGcSupplyThenStop;
                addon->FireCallbackInt(-1);
                return;
            }

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
        if (SelectSelectYesno(0, s => s == "Do you really want to trade a high-quality item?"))
            return;

        if (TryGetAddonByName<AddonGrandCompanySupplyReward>("GrandCompanySupplyReward",
                out var addonSupplyReward) && IsAddonReady(&addonSupplyReward->AtkUnitBase))
        {
            addonSupplyReward->AtkUnitBase.FireCallbackInt(0);
            _continueAt = DateTime.Now.AddSeconds(0.58);
            CurrentStage = Stage.FinalizeTurnIn;
        }
    }

    private unsafe void FinalizeTurnInItem()
    {
        if (TryGetAddonByName<AddonGrandCompanySupplyList>("GrandCompanySupplyList",
                out var addonSupplyList) && IsAddonReady(&addonSupplyList->AtkUnitBase))
        {
            var updateFilter = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 5 },
                new() { Type = ValueType.Int, Int = (int)_configuration.ItemFilter },
                new() { Type = 0, Int = 0 }
            };
            addonSupplyList->AtkUnitBase.FireCallback(3, updateFilter);
            CurrentStage = Stage.SelectItemToTurnIn;
        }
    }

    private void CloseGcSupply()
    {
        if (SelectSelectString(3))
        {
            if (!_selectedRewardItem.IsValid())
            {
                _turnInWindow.State = false;
                CurrentStage = Stage.RequestStop;
            }
            else
            {
                // you can occasionally get a 'not enough seals' warning lol
                _continueAt = DateTime.Now.AddSeconds(1);
                CurrentStage = Stage.TargetQuartermaster;
            }
        }
    }

    private void CloseGcSupplyThenStop()
    {
        if (SelectSelectString(3))
        {
            if (!_selectedRewardItem.IsValid())
            {
                _turnInWindow.State = false;
                CurrentStage = Stage.RequestStop;
            }
            else if (GetCurrentSealCount() <=
                     _configuration.ReservedSealCount + _selectedRewardItem.SealCost)
            {
                _turnInWindow.State = false;
                CurrentStage = Stage.RequestStop;
            }
            else
            {
                _continueAt = DateTime.Now.AddSeconds(1);
                CurrentStage = Stage.TargetQuartermaster;
            }
        }
    }
}
