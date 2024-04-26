using System;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Component.GUI;
using LLib.GameUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Deliveroo.Handlers;

internal sealed class ExchangeHandler
{
    private readonly DeliverooPlugin _plugin;
    private readonly GameFunctions _gameFunctions;
    private readonly ITargetManager _targetManager;
    private readonly IGameGui _gameGui;
    private readonly IChatGui _chatGui;
    private readonly IPluginLog _pluginLog;

    public ExchangeHandler(DeliverooPlugin plugin, GameFunctions gameFunctions, ITargetManager targetManager,
        IGameGui gameGui, IChatGui chatGui, IPluginLog pluginLog)
    {
        _plugin = plugin;
        _gameFunctions = gameFunctions;
        _targetManager = targetManager;
        _gameGui = gameGui;
        _chatGui = chatGui;
        _pluginLog = pluginLog;
    }

    public void InteractWithQuartermaster(GameObject personnelOfficer, GameObject quartermaster)
    {
        if (_gameFunctions.GetCurrentSealCount() < _plugin.EffectiveReservedSealCount)
        {
            _plugin.CurrentStage = Stage.RequestStop;
            return;
        }

        if (_targetManager.Target == personnelOfficer)
            return;

        _gameFunctions.InteractWithTarget(quartermaster);
        _plugin.CurrentStage = Stage.SelectRewardTier;
    }

    public PurchaseItemRequest? GetNextItemToPurchase(PurchaseItemRequest? previousRequest = null)
    {
        foreach (PurchaseItemRequest request in _plugin.ItemsToPurchaseNow)
        {
            int toBuy = 0;
            if (request == previousRequest)
            {
                toBuy = (int)request.StackSize;
                if (request.ItemId != ItemIds.Venture)
                    toBuy = Math.Min(toBuy, 99);
            }

            if (request.Type == Configuration.PurchaseType.KeepStocked)
            {
                if (_gameFunctions.GetItemCount(request.ItemId, request.CheckRetainerInventory) + toBuy <
                    request.EffectiveLimit)
                    return request;
            }
            else
            {
                if (toBuy < request.EffectiveLimit)
                    return request;
            }
        }

        return null;
    }

    public unsafe void SelectRewardTier()
    {
        PurchaseItemRequest? item = GetNextItemToPurchase();
        if (item == null)
        {
            _plugin.CurrentStage = Stage.CloseGcExchange;
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
            _plugin.ContinueAt = DateTime.Now.AddSeconds(0.5);
            _plugin.CurrentStage = Stage.SelectRewardSubCategory;
        }
    }

    public unsafe void SelectRewardSubCategory()
    {
        PurchaseItemRequest? item = GetNextItemToPurchase();
        if (item == null)
        {
            _plugin.CurrentStage = Stage.CloseGcExchange;
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
            _plugin.ContinueAt = DateTime.Now.AddSeconds(0.5);
            _plugin.CurrentStage = Stage.SelectReward;
        }
    }

    public unsafe void SelectReward()
    {
        if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            LAddon.IsAddonReady(addonExchange))
        {
            if (SelectRewardItem(addonExchange))
            {
                _plugin.ContinueAt = DateTime.Now.AddSeconds(0.2);
                _plugin.CurrentStage = Stage.ConfirmReward;
            }
            else
            {
                _plugin.ContinueAt = DateTime.Now.AddSeconds(0.2);
                _plugin.CurrentStage = Stage.CloseGcExchange;
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
                long toBuy = (_gameFunctions.GetCurrentSealCount() - _plugin.EffectiveReservedSealCount) /
                             item.SealCost;
                if (item.Type == Configuration.PurchaseType.KeepStocked)
                    toBuy = Math.Min(toBuy,
                        item.EffectiveLimit - _gameFunctions.GetItemCount(item.ItemId, item.CheckRetainerInventory));
                else
                    toBuy = Math.Min(toBuy, item.EffectiveLimit);

                if (item.ItemId != ItemIds.Venture)
                    toBuy = Math.Min(toBuy, 99);

                if (toBuy <= 0)
                {
                    _pluginLog.Information($"Items to buy = {toBuy}");
                    return false;
                }

                item.TemporaryPurchaseQuantity = toBuy;
                _chatGui.Print(new SeString(new TextPayload($"Buying {toBuy}x "))
                    .Append(SeString.CreateItemLink(item.ItemId))
                    .Append(new TextPayload("...")));
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

    public unsafe void CloseGcExchange()
    {
        if (_gameGui.TryGetAddonByName<AtkUnitBase>("GrandCompanyExchange", out var addonExchange) &&
            LAddon.IsAddonReady(addonExchange))
        {
            addonExchange->FireCallbackInt(-1);

            // If we just turned in the final item, there's no need to talk to the personnel officer again
            if (_plugin.LastTurnInListSize == 1)
            {
                _plugin.TurnInState = false;
                _plugin.CurrentStage = Stage.RequestStop;
            }
            else
            {
                _plugin.ContinueAt = DateTime.Now.AddSeconds(1);
                _plugin.CurrentStage = Stage.TargetPersonnelOfficer;
            }
        }
    }
}
