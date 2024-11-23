using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using LLib;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using Lumina.Text.ReadOnly;

namespace Deliveroo.GameData;

internal sealed class GameStrings
{
    public GameStrings(IDataManager dataManager, IPluginLog pluginLog)
    {
        UndertakeSupplyAndProvisioningMission =
            dataManager.GetString<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_002", pluginLog)
            ?? throw new ConstraintException($"Unable to resolve {nameof(UndertakeSupplyAndProvisioningMission)}");
        ClosePersonnelOfficerTalk =
            dataManager.GetString<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_004", pluginLog)
            ?? throw new ConstraintException($"Unable to resolve {nameof(ClosePersonnelOfficerTalk)}");
        ExchangeItems = dataManager.GetRegex<Addon>(3290, addon => addon.Text, pluginLog)
                        ?? throw new ConstraintException($"Unable to resolve {nameof(ExchangeItems)}");
        TradeHighQualityItem =
            dataManager.GetString<Addon>(102434, addon => addon.Text, pluginLog)
            ?? throw new ConstraintException($"Unable to resolve {nameof(TradeHighQualityItem)}");

        var rankUpFc = dataManager.GetExcelSheet<LogMessage>().GetRow(3123);
        RankUpFc = rankUpFc.GetRegex(logMessage => logMessage.Text, pluginLog)
                   ?? throw new ConstraintException($"Unable to resolve {nameof(RankUpFc)}");
        RankUpFcType = (XivChatType)rankUpFc.LogKind;
    }


    public string UndertakeSupplyAndProvisioningMission { get; }
    public string ClosePersonnelOfficerTalk { get; }
    public Regex ExchangeItems { get; }
    public string TradeHighQualityItem { get; }
    public Regex RankUpFc { get; }
    public XivChatType RankUpFcType { get; }

    [Sheet("custom/000/ComDefGrandCompanyOfficer_00073")]
    [SuppressMessage("Performance", "CA1812")]
    private readonly struct ComDefGrandCompanyOfficer(ExcelPage page, uint offset, uint row)
        : IQuestDialogueText, IExcelRow<ComDefGrandCompanyOfficer>
    {
        public uint RowId => row;

        public ReadOnlySeString Key => page.ReadString(offset, offset);
        public ReadOnlySeString Value => page.ReadString(offset + 4, offset);

        static ComDefGrandCompanyOfficer IExcelRow<ComDefGrandCompanyOfficer>.Create(ExcelPage page, uint offset,
            uint row) =>
            new(page, offset, row);
    }
}
