using System;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Text.SeStringHandling;
using Deliveroo.GameData;
using FFXIVClientStructs.FFXIV.Client.UI;
using LLib.GameUI;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private unsafe void GrandCompanySupplyRewardPostSetup(AddonEvent type, AddonArgs args)
    {
        bool quickTurnIn = CurrentStage == Stage.Stopped && _keyState[_configuration.QuickTurnInKey];
        if (CurrentStage == Stage.TurnInSelected || quickTurnIn)
        {
            AddonGrandCompanySupplyReward* addonSupplyReward = (AddonGrandCompanySupplyReward*)args.Addon;

            string? itemName = addonSupplyReward->AtkUnitBase.AtkValues[4].ReadAtkString();
            if (itemName != null && _itemCache.GetItemIdFromItemName(itemName)
                    .Any(itemId => InternalConfiguration.QuickVentureExclusiveItems.Contains(itemId)))
            {
                DeliveryResult = new MessageDeliveryResult
                {
                    Message = new SeStringBuilder()
                        .Append("Won't turn in ")
                        .AddItemLink(_itemCache.GetItemIdFromItemName(itemName).First())
                        .Append(", as it can only be obtained through Quick Ventures.")
                        .Build(),
                };

                addonSupplyReward->AtkUnitBase.FireCallbackInt(1);
                if (quickTurnIn)
                    CurrentStage = Stage.RequestStop;
                else
                    CurrentStage = Stage.CloseGcSupplyWindowThenStop;
                return;
            }

            _pluginLog.Information($"Turning in '{itemName}'");

            addonSupplyReward->AtkUnitBase.FireCallbackInt(0);
            ContinueAt = DateTime.Now.AddSeconds(0.58);
            if (quickTurnIn)
            {
                DeliveryResult = new NoDeliveryResult();
                CurrentStage = Stage.SingleFinalizeTurnIn;
            }
            else
                CurrentStage = Stage.FinalizeTurnIn;
        }
    }
}
