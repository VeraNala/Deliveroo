using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Deliveroo.External;

internal sealed class ExternalPluginHandler
{
    private readonly IPluginLog _pluginLog;
    private readonly YesAlreadyIpc _yesAlreadyIpc;
    private readonly PandoraIpc _pandoraIpc;

    private bool? _yesAlreadyState;
    private bool? _pandoraState;

    public ExternalPluginHandler(DalamudPluginInterface pluginInterface, IFramework framework, IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;

        var dalamudReflector = new DalamudReflector(pluginInterface, framework, pluginLog);
        _yesAlreadyIpc = new YesAlreadyIpc(dalamudReflector);
        _pandoraIpc = new PandoraIpc(pluginInterface, pluginLog);
    }

    public bool Saved { get; private set; }

    public void Save()
    {
        if (Saved)
        {
            _pluginLog.Information("Not overwriting external plugin state");
            return;
        }

        _pluginLog.Information("Saving external plugin state...");
        SaveYesAlreadyState();
        SavePandoraState();
        Saved = true;
    }

    private void SaveYesAlreadyState()
    {
        _yesAlreadyState = _yesAlreadyIpc.DisableIfNecessary();
        _pluginLog.Information($"Previous yesalready state: {_yesAlreadyState}");
    }

    private void SavePandoraState()
    {
        _pandoraState = _pandoraIpc.DisableIfNecessary();
        _pluginLog.Info($"Previous pandora feature state: {_pandoraState}");
    }

    public void Restore()
    {
        if (Saved)
        {
            RestoreYesAlready();
            RestorePandora();
        }

        Saved = false;
        _yesAlreadyState = null;
        _pandoraState = null;
    }

    private void RestoreYesAlready()
    {
        _pluginLog.Information($"Restoring previous yesalready state: {_yesAlreadyState}");
        if (_yesAlreadyState == true)
            _yesAlreadyIpc.Enable();
    }

    private void RestorePandora()
    {
        _pluginLog.Information($"Restoring previous pandora state: {_pandoraState}");
        if (_pandoraState == true)
            _pandoraIpc.Enable();
    }
}
