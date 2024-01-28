using System;
using System.Collections.Generic;
using ARControl.External;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Deliveroo.External;

internal sealed class ExternalPluginHandler : IDisposable
{
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly IPluginLog _pluginLog;
    private readonly DeliverooIpc _deliverooIpc;
    private readonly PandoraIpc _pandoraIpc;
    private readonly AllaganToolsIpc _allaganToolsIpc;

    private bool? _pandoraState;

    public ExternalPluginHandler(DalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _pluginLog = pluginLog;
        _deliverooIpc = new DeliverooIpc(pluginInterface);
        _pandoraIpc = new PandoraIpc(pluginInterface, pluginLog);
        _allaganToolsIpc = new AllaganToolsIpc(pluginInterface, pluginLog);
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
        _deliverooIpc.StartTurnIn();
        SaveYesAlreadyState();
        SavePandoraState();
        Saved = true;
    }

    private void SaveYesAlreadyState()
    {
        if (_pluginInterface.TryGetData<HashSet<string>>("YesAlready.StopRequests", out var data) &&
            !data.Contains(nameof(Deliveroo)))
        {
            _pluginLog.Debug("Disabling YesAlready");
            data.Add(nameof(Deliveroo));
        }
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
        _pandoraState = null;
        _deliverooIpc.StopTurnIn();
    }

    private void RestoreYesAlready()
    {
        if (_pluginInterface.TryGetData<HashSet<string>>("YesAlready.StopRequests", out var data) &&
            data.Contains(nameof(Deliveroo)))
        {
            _pluginLog.Debug("Restoring YesAlready");
            data.Remove(nameof(Deliveroo));
        }
    }

    private void RestorePandora()
    {
        _pluginLog.Information($"Restoring previous pandora state: {_pandoraState}");
        if (_pandoraState == true)
            _pandoraIpc.Enable();
    }

    public void Dispose()
    {
        _deliverooIpc.Dispose();
    }

    public uint GetRetainerItemCount(uint itemId) => _allaganToolsIpc.GetRetainerItemCount(itemId);
}
