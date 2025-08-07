using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI;
using LLib.GameUI;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private unsafe void SelectStringPostSetup(AddonEvent type, AddonArgs args)
    {
        AddonSelectString* addonSelectString = (AddonSelectString*)args.Addon.Address;
        SelectStringPostSetup(addonSelectString, CurrentStage);
    }

    private unsafe bool SelectStringPostSetup(AddonSelectString* addonSelectString, Stage stage)
    {
        _pluginLog.Verbose("SelectString post-setup");

        string desiredText;
        Action followUp;
        if (stage == Stage.OpenGcSupply)
        {
            desiredText = _gameStrings.UndertakeSupplyAndProvisioningMission;
            followUp = OpenGcSupplySelectStringFollowUp;
        }
        else if (stage == Stage.CloseGcSupplySelectString)
        {
            desiredText = _gameStrings.ClosePersonnelOfficerTalk;
            followUp = CloseGcSupplySelectStringFollowUp;
        }
        else if (stage == Stage.CloseGcSupplySelectStringThenStop)
        {
            desiredText = _gameStrings.ClosePersonnelOfficerTalk;
            followUp = CloseGcSupplySelectStringThenStopFollowUp;
        }
        else
            return false;

        _pluginLog.Verbose($"Looking for '{desiredText}' in prompt");
        for (ushort i = 7; i < addonSelectString->AtkUnitBase.AtkValuesCount; ++i)
        {
            if (addonSelectString->AtkUnitBase.AtkValues[i].Type == ValueType.String)
            {
                int choice = i - 7;
                string? text = addonSelectString->AtkUnitBase.AtkValues[i].ReadAtkString();
                _pluginLog.Verbose($"  Choice {choice} → {text}");
                if (text == desiredText)
                {
                    _pluginLog.Information($"Selecting choice {choice} ({text})");
                    addonSelectString->AtkUnitBase.FireCallbackInt(choice);

                    followUp();
                    return true;
                }
            }
        }

        _pluginLog.Verbose($"Text '{desiredText}' was not found in prompt.");
        return false;
    }

    private void OpenGcSupplySelectStringFollowUp()
    {
        _supplyHandler.ResetTurnInErrorHandling();
        CurrentStage = Stage.SelectExpertDeliveryTab;
    }

    private void CloseGcSupplySelectStringFollowUp()
    {
        if (_exchangeHandler.GetNextItemToPurchase() == null)
        {
            _turnInWindow.State = false;
            CurrentStage = Stage.RequestStop;
        }
        else
        {
            // you can occasionally get a 'not enough seals' warning lol
            ContinueAt = DateTime.Now.AddSeconds(1);
            CurrentStage = Stage.TargetQuartermaster;
        }
    }

    private void CloseGcSupplySelectStringThenStopFollowUp()
    {
        if (_exchangeHandler.GetNextItemToPurchase() == null)
        {
            _turnInWindow.State = false;
            CurrentStage = Stage.RequestStop;
        }
        else if (_gameFunctions.GetCurrentSealCount() <=
                 EffectiveReservedSealCount + _exchangeHandler.GetNextItemToPurchase()!.SealCost)
        {
            _turnInWindow.State = false;
            CurrentStage = Stage.RequestStop;
        }
        else
        {
            ContinueAt = DateTime.Now.AddSeconds(1);
            CurrentStage = Stage.TargetQuartermaster;
        }
    }
}
