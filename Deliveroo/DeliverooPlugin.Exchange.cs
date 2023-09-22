using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private void InteractWithQuartermaster(GameObject personnelOfficer, GameObject quartermaster)
    {
        if (GetCurrentSealCount() < _configuration.ReservedSealCount)
        {
            CurrentStage = Stage.RequestStop;
            return;
        }

        if (_targetManager.Target == personnelOfficer)
            return;

        InteractWithTarget(quartermaster);
        CurrentStage = Stage.SelectRewardTier;
    }

    private unsafe void SelectRewardTier()
    {
        if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            IsAddonReady(addonExchange))
        {
            PluginLog.Information($"Selecting tier 1, {(int)_selectedRewardItem.Tier - 1}");
            var selectRank = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 1 },
                new() { Type = ValueType.Int, Int = (int)_selectedRewardItem.Tier - 1 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 }
            };
            addonExchange->FireCallback(9, selectRank);
            _continueAt = DateTime.Now.AddSeconds(0.5);
            CurrentStage = Stage.SelectRewardSubCategory;
        }
    }

    private unsafe void SelectRewardSubCategory()
    {
        if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            IsAddonReady(addonExchange))
        {
            PluginLog.Information($"Selecting subcategory 2, {(int)_selectedRewardItem.SubCategory}");
            var selectType = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 2 },
                new() { Type = ValueType.Int, Int = (int)_selectedRewardItem.SubCategory },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 },
                new() { Type = 0, Int = 0 }
            };
            addonExchange->FireCallback(9, selectType);
            _continueAt = DateTime.Now.AddSeconds(0.5);
            CurrentStage = Stage.SelectReward;
        }
    }

    private unsafe void SelectReward()
    {
        if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            IsAddonReady(addonExchange))
        {
            if (SelectRewardItem(addonExchange))
            {
                _continueAt = DateTime.Now.AddSeconds(0.5);
                CurrentStage = Stage.ConfirmReward;
            }
            else
            {
                PluginLog.Warning("Could not find selected reward item");
                _continueAt = DateTime.Now.AddSeconds(0.5);
                CurrentStage = Stage.CloseGcExchange;
            }
        }
    }

    private void ConfirmReward()
    {
        if (SelectSelectYesno(0, s => s.StartsWith("Exchange ")))
        {
            CurrentStage = Stage.CloseGcExchange;
            _continueAt = DateTime.Now.AddSeconds(0.5);
        }
    }

    private unsafe void CloseGcExchange()
    {
        if (TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            IsAddonReady(addonExchange))
        {
            addonExchange->FireCallbackInt(-1);
            CurrentStage = Stage.TargetPersonnelOfficer;
        }
    }
}
