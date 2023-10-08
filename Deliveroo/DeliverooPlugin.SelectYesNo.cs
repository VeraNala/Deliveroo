using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private unsafe void SelectYesNoPostSetup(AddonEvent type, AddonArgs args)
    {
        _pluginLog.Verbose("SelectYesNo post-setup");

        AddonSelectYesno* addonSelectYesNo = (AddonSelectYesno*)args.Addon;
        string text = MemoryHelper.ReadSeString(&addonSelectYesNo->PromptText->NodeText).ToString().Replace("\n", "").Replace("\r", "");
        _pluginLog.Verbose($"YesNo prompt: '{text}'");

        if (CurrentStage == Stage.ConfirmReward &&
            _gameStrings.ExchangeItems.IsMatch(text))
        {
            PurchaseItemRequest? item = GetNextItemToPurchase();
            if (item == null)
            {
                addonSelectYesNo->AtkUnitBase.FireCallbackInt(1);
                CurrentStage = Stage.CloseGcExchange;
                return;
            }

            _pluginLog.Information($"Selecting 'yes' ({text})");
            addonSelectYesNo->AtkUnitBase.FireCallbackInt(0);

            var nextItem = GetNextItemToPurchase(item);
            if (nextItem != null && GetCurrentSealCount() >= _configuration.ReservedSealCount + nextItem.SealCost)
                CurrentStage = Stage.SelectRewardTier;
            else
                CurrentStage = Stage.CloseGcExchange;
            _continueAt = DateTime.Now.AddSeconds(0.5);
        }
        else if (CurrentStage == Stage.TurnInSelected &&
                 _gameStrings.TradeHighQualityItem == text)
        {
            _pluginLog.Information($"Selecting 'yes' ({text})");
            addonSelectYesNo->AtkUnitBase.FireCallbackInt(0);
        }
    }
}
