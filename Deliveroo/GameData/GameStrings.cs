using System;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
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
            dataManager.GetString<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_002", pluginLog)
            ?? throw new Exception($"Unable to resolve {nameof(UndertakeSupplyAndProvisioningMission)}");
        ClosePersonnelOfficerTalk =
            dataManager.GetString<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_004", pluginLog)
            ?? throw new Exception($"Unable to resolve {nameof(ClosePersonnelOfficerTalk)}");
        ExchangeItems = dataManager.GetRegex<Addon>(3290, addon => addon.Text, pluginLog)
                        ?? throw new Exception($"Unable to resolve {nameof(ExchangeItems)}");
        TradeHighQualityItem = dataManager.GetString<Addon>(102434, addon => addon.Text, pluginLog)
                               ?? throw new Exception($"Unable to resolve {nameof(TradeHighQualityItem)}");

        var rankUpFc = dataManager.GetExcelSheet<LogMessage>()!.GetRow(3123)!;
        RankUpFc = rankUpFc.GetRegex(logMessage => logMessage.Text, pluginLog)
                   ?? throw new Exception($"Unable to resolve {nameof(RankUpFc)}");
        RankUpFcType = (XivChatType)rankUpFc.LogKind;
    }


    public string UndertakeSupplyAndProvisioningMission { get; }
    public string ClosePersonnelOfficerTalk { get; }
    public Regex ExchangeItems { get; }
    public string TradeHighQualityItem { get; }
    public Regex RankUpFc { get; }
    public XivChatType RankUpFcType { get; }

    [Sheet("custom/000/ComDefGrandCompanyOfficer_00073")]
    private class ComDefGrandCompanyOfficer : QuestDialogueText
    {
    }
}
