using System;
using System.Collections.Generic;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Deliveroo.External;

internal sealed class ExternalPluginHandler : IDisposable
{
    private readonly DalamudPluginInterface _pluginInterface;
    private readonly IGameConfig _gameConfig;
    private readonly Configuration _configuration;
    private readonly IPluginLog _pluginLog;
    private readonly DeliverooIpc _deliverooIpc;
    private readonly PandoraIpc _pandoraIpc;
    private readonly AllaganToolsIpc _allaganToolsIpc;

    private bool? _pandoraState;
    private ConfigState? _configState;

    public ExternalPluginHandler(DalamudPluginInterface pluginInterface, IGameConfig gameConfig,
        Configuration configuration, IPluginLog pluginLog)
    {
        _pluginInterface = pluginInterface;
        _gameConfig = gameConfig;
        _configuration = configuration;
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
        SaveGameConfig();
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

    private void SaveGameConfig()
    {
        if (_configuration.DisableFrameLimiter)
            _configState = new ConfigState(_gameConfig);
    }

    public void Restore()
    {
        if (Saved)
        {
            RestoreYesAlready();
            RestorePandora();
            RestoreGameConfig();
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

    private void RestoreGameConfig()
    {
        _configState?.Restore(_gameConfig);
        _configState = null;
    }

    public void Dispose()
    {
        _deliverooIpc.Dispose();
    }

    public uint GetRetainerItemCount(uint itemId) => _allaganToolsIpc.GetRetainerItemCount(itemId);

    private sealed record ConfigState(uint Fps, uint FpsInactive)
    {
        private const string ConfigFps = "Fps";
        private const string ConfigFpsInactive = "FPSInActive";

        public ConfigState(IGameConfig gameConfig)
        : this(gameConfig.System.GetUInt(ConfigFps), gameConfig.System.GetUInt(ConfigFpsInactive))
        {
            gameConfig.System.Set(ConfigFps, 0);
            gameConfig.System.Set(ConfigFpsInactive, 0);
        }

        public void Restore(IGameConfig gameConfig)
        {
            gameConfig.System.Set(ConfigFps, Fps);
            gameConfig.System.Set(ConfigFpsInactive, FpsInactive);
        }
    }
}
