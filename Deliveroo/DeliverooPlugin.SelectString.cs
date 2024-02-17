using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Deliveroo;

partial class DeliverooPlugin
{
    private unsafe void SelectStringPostSetup(AddonEvent type, AddonArgs args)
    {
        _pluginLog.Verbose("SelectString post-setup");

        string desiredText;
        Action followUp;
        if (CurrentStage == Stage.OpenGcSupply)
        {
            desiredText = _gameStrings.UndertakeSupplyAndProvisioningMission;
            followUp = OpenGcSupplySelectStringFollowUp;
        }
        else if (CurrentStage == Stage.CloseGcSupplySelectString)
        {
            desiredText = _gameStrings.ClosePersonnelOfficerTalk;
            followUp = CloseGcSupplySelectStringFollowUp;
        }
        else if (CurrentStage == Stage.CloseGcSupplySelectStringThenStop)
        {
            desiredText = _gameStrings.ClosePersonnelOfficerTalk;
            followUp = CloseGcSupplySelectStringThenStopFollowUp;
        }
        else
            return;

        _pluginLog.Verbose($"Looking for '{desiredText}' in prompt");
        AddonSelectString* addonSelectString = (AddonSelectString*)args.Addon;
        int entries = addonSelectString->PopupMenu.PopupMenu.EntryCount;

        for (int i = 0; i < entries; ++i)
        {
            var textPointer = addonSelectString->PopupMenu.PopupMenu.EntryNames[i];
            if (textPointer == null)
                continue;

            var text = MemoryHelper.ReadSeStringNullTerminated((nint)textPointer).ToString();
            _pluginLog.Verbose($"  Choice {i} → {text}");
            if (text == desiredText)
            {

                _pluginLog.Information($"Selecting choice {i} ({text})");
                addonSelectString->AtkUnitBase.FireCallbackInt(i);

                followUp();
                return;
            }
        }

        _pluginLog.Verbose($"Text '{desiredText}' was not found in prompt.");
    }

    private void OpenGcSupplySelectStringFollowUp()
    {
        ResetTurnInErrorHandling();
        CurrentStage = Stage.SelectExpertDeliveryTab;
    }

    private void CloseGcSupplySelectStringFollowUp()
    {
        if (GetNextItemToPurchase() == null)
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

    private void CloseGcSupplySelectStringThenStopFollowUp()
    {
        if (GetNextItemToPurchase() == null)
        {
            _turnInWindow.State = false;
            CurrentStage = Stage.RequestStop;
        }
        else if (GetCurrentSealCount() <= EffectiveReservedSealCount + GetNextItemToPurchase()!.SealCost)
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
