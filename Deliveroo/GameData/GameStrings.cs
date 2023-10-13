using System;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using LLib;
using Lumina.Excel;
using Lumina.Excel.CustomSheets;
using Lumina.Excel.GeneratedSheets;

namespace Deliveroo.GameData;

internal sealed class GameStrings
{
    public GameStrings(IDataManager dataManager, IPluginLog pluginLog)
    {
        UndertakeSupplyAndProvisioningMission =
            dataManager.GetDialogue<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_002", pluginLog);
        ClosePersonnelOfficerTalk =
            dataManager.GetDialogue<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_004", pluginLog);
        ExchangeItems = dataManager.GetRegex<Addon>(4928, addon => addon.Text, pluginLog)
                        ?? throw new Exception($"Unable to resolve {nameof(ExchangeItems)}");
        TradeHighQualityItem = dataManager.GetString<Addon>(102434, addon => addon.Text, pluginLog)
                               ?? throw new Exception($"Unable to resolve {nameof(TradeHighQualityItem)}");
    }


    public string UndertakeSupplyAndProvisioningMission { get; }
    public string ClosePersonnelOfficerTalk { get; }
    public Regex ExchangeItems { get; }
    public string TradeHighQualityItem { get; }

    [Sheet("custom/000/ComDefGrandCompanyOfficer_00073")]
    private class ComDefGrandCompanyOfficer : QuestDialogueText
    {
    }
}
