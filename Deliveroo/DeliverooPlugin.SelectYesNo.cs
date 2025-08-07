using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private unsafe void SelectYesNoPostSetup(AddonEvent type, AddonArgs args)
    {
        _pluginLog.Verbose("SelectYesNo post-setup");

        AddonSelectYesno* addonSelectYesNo = (AddonSelectYesno*)args.Addon.Address;
        string text = MemoryHelper.ReadSeString(&addonSelectYesNo->PromptText->NodeText).ToString().ReplaceLineEndings("");
        _pluginLog.Verbose($"YesNo prompt: '{text}'");

        if (CurrentStage == Stage.ConfirmReward &&
            _gameStrings.ExchangeItems.IsMatch(text))
        {
            PurchaseItemRequest? item = _exchangeHandler.GetNextItemToPurchase();
            if (item == null)
            {
                addonSelectYesNo->AtkUnitBase.FireCallbackInt(1);
                CurrentStage = Stage.CloseGcExchange;
                return;
            }

            _pluginLog.Information($"Selecting 'yes' ({text}) (callback = {item.OnPurchase}, qty = {item.TemporaryPurchaseQuantity})");
            addonSelectYesNo->AtkUnitBase.FireCallbackInt(0);

            item.OnPurchase?.Invoke((int)item.TemporaryPurchaseQuantity);
            item.TemporaryPurchaseQuantity = 0;

            var nextItem = _exchangeHandler.GetNextItemToPurchase(item);
            if (nextItem != null && _gameFunctions.GetCurrentSealCount() >= EffectiveReservedSealCount + nextItem.SealCost)
                CurrentStage = Stage.SelectRewardTier;
            else
                CurrentStage = Stage.CloseGcExchange;
            ContinueAt = DateTime.Now.AddSeconds(0.5);
        }
        else if ((CurrentStage == Stage.TurnInSelected || (_configuration.QuickTurnInKey != VirtualKey.NO_KEY && _keyState[_configuration.QuickTurnInKey])) &&
                 _gameStrings.TradeHighQualityItem == text)
        {
            _pluginLog.Information($"Selecting 'yes' ({text})");
            addonSelectYesNo->AtkUnitBase.FireCallbackInt(0);
        }
    }
}
