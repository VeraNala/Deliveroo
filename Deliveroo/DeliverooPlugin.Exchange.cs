using System;
using Dalamud.Game.ClientState.Objects.Types;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
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

    private PurchaseItemRequest? GetNextItemToPurchase(PurchaseItemRequest? previousRequest = null)
    {
        foreach (PurchaseItemRequest request in _itemsToPurchaseNow)
        {
            int offset = 0;
            if (request == previousRequest)
                offset = (int)request.StackSize;

            if (GetItemCount(request.ItemId) + offset < request.EffectiveLimit)
                return request;
        }

        return null;
    }

    private unsafe void SelectRewardTier()
    {
        PurchaseItemRequest? item = GetNextItemToPurchase();
        if (item == null)
        {
            CurrentStage = Stage.CloseGcExchange;
            return;
        }

        if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            LAddon.IsAddonReady(addonExchange))
        {
            _pluginLog.Information($"Selecting tier 1, {(int)item.Tier - 1}");
            var selectRank = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 1 },
                new() { Type = ValueType.Int, Int = (int)item.Tier - 1 },
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
        PurchaseItemRequest? item = GetNextItemToPurchase();
        if (item == null)
        {
            CurrentStage = Stage.CloseGcExchange;
            return;
        }

        if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            LAddon.IsAddonReady(addonExchange))
        {
            _pluginLog.Information($"Selecting subcategory 2, {(int)item.SubCategory}");
            var selectType = stackalloc AtkValue[]
            {
                new() { Type = ValueType.Int, Int = 2 },
                new() { Type = ValueType.Int, Int = (int)item.SubCategory },
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
        if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            LAddon.IsAddonReady(addonExchange))
        {
            if (SelectRewardItem(addonExchange))
            {
                _continueAt = DateTime.Now.AddSeconds(0.2);
                CurrentStage = Stage.ConfirmReward;
            }
            else
            {
                _continueAt = DateTime.Now.AddSeconds(0.2);
                CurrentStage = Stage.CloseGcExchange;
            }
        }
    }

    private unsafe bool SelectRewardItem(AtkUnitBase* addonExchange)
    {
        PurchaseItemRequest? item = GetNextItemToPurchase();
        if (item == null)
            return false;

        uint itemsOnCurrentPage = addonExchange->AtkValues[1].UInt;
        for (uint i = 0; i < itemsOnCurrentPage; ++i)
        {
            uint itemId = addonExchange->AtkValues[317 + i].UInt;
            if (itemId == item.ItemId)
            {
                _pluginLog.Information($"Selecting item {itemId}, {i}");
                long toBuy = (GetCurrentSealCount() - _configuration.ReservedSealCount) / item.SealCost;
                toBuy = Math.Min(toBuy, item.EffectiveLimit - GetItemCount(item.ItemId));

                if (item.ItemId != ItemIds.Venture && !_configuration.IgnoreCertainLimitations)
                    toBuy = Math.Min(toBuy, 99);

                if (toBuy <= 0)
                {
                    _pluginLog.Information($"Items to buy = {toBuy}");
                    return false;
                }

                _chatGui.Print($"Buying {toBuy}x {item.Name}...");
                var selectReward = stackalloc AtkValue[]
                {
                    new() { Type = ValueType.Int, Int = 0 },
                    new() { Type = ValueType.Int, Int = (int)i },
                    new() { Type = ValueType.Int, Int = (int)toBuy },
                    new() { Type = 0, Int = 0 },
                    new() { Type = ValueType.Bool, Byte = 1 },
                    new() { Type = ValueType.Bool, Byte = 0 },
                    new() { Type = 0, Int = 0 },
                    new() { Type = 0, Int = 0 },
                    new() { Type = 0, Int = 0 }
                };
                addonExchange->FireCallback(9, selectReward);
                return true;
            }
        }

        _pluginLog.Warning("Could not find selected reward item");
        return false;
    }

    private unsafe void CloseGcExchange()
    {
        if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            LAddon.IsAddonReady(addonExchange))
        {
            addonExchange->FireCallbackInt(-1);
            CurrentStage = Stage.TargetPersonnelOfficer;
        }
    }
}
