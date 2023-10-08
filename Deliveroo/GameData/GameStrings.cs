using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.CustomSheets;
using Lumina.Excel.GeneratedSheets;
using Lumina.Text;
using Lumina.Text.Payloads;

namespace Deliveroo.GameData;

internal sealed class GameStrings
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _pluginLog;

    public GameStrings(IDataManager dataManager, IPluginLog pluginLog)
    {
        _dataManager = dataManager;
        _pluginLog = pluginLog;

        UndertakeSupplyAndProvisioningMission =
            GetDialogue<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_002");
        ClosePersonnelOfficerTalk =
            GetDialogue<ComDefGrandCompanyOfficer>("TEXT_COMDEFGRANDCOMPANYOFFICER_00073_A4_004");
        ExchangeItems = GetRegex<Addon>(4928, addon => addon.Text)
                        ?? throw new Exception($"Unable to resolve {nameof(ExchangeItems)}");
        TradeHighQualityItem = GetString<Addon>(102434, addon => addon.Text)
                               ?? throw new Exception($"Unable to resolve {nameof(TradeHighQualityItem)}");
    }


    public string UndertakeSupplyAndProvisioningMission { get; }
    public string ClosePersonnelOfficerTalk { get; }
    public Regex ExchangeItems { get; }
    public string TradeHighQualityItem { get; }

    private string GetDialogue<T>(string key)
        where T : QuestDialogueText
    {
        string result = _dataManager.GetExcelSheet<T>()!
            .Single(x => x.Key == key)
            .Value
            .ToString();
        _pluginLog.Verbose($"{typeof(T).Name}.{key} => {result}");
        return result;
    }

    private SeString? GetSeString<T>(uint rowId, Func<T, SeString?> mapper)
        where T : ExcelRow
    {
        var row = _dataManager.GetExcelSheet<T>()?.GetRow(rowId);
        if (row == null)
            return null;

        return mapper(row);
    }

    private string? GetString<T>(uint rowId, Func<T, SeString?> mapper)
        where T : ExcelRow
    {
        string? text = GetSeString(rowId, mapper)?.ToString();

        _pluginLog.Verbose($"{typeof(T).Name}.{rowId} => {text}");
        return text;
    }

    private Regex? GetRegex<T>(uint rowId, Func<T, SeString?> mapper)
        where T : ExcelRow
    {
        SeString? text = GetSeString(rowId, mapper);
        string regex = string.Join("", text.Payloads.Select(payload =>
        {
            if (payload is TextPayload)
                return Regex.Escape(payload.RawString);
            else
                return ".*";
        }));
        _pluginLog.Verbose($"{typeof(T).Name}.{rowId} => /{regex}/");
        return new Regex(regex);
    }

    [Sheet("custom/000/ComDefGrandCompanyOfficer_00073")]
    private class ComDefGrandCompanyOfficer : QuestDialogueText
    {
    }
}
