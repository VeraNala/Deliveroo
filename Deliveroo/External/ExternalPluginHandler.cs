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
    private SystemConfigState? _limitFrameRateWhenClientInactive;
    private SystemConfigState? _uncapFrameRate;

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
        if (!_configuration.DisableFrameLimiter)
            return;

        _limitFrameRateWhenClientInactive ??=
            new SystemConfigState(_gameConfig, SystemConfigState.ConfigFpsInactive, 0);

        if (_configuration.UncapFrameRate)
            _uncapFrameRate ??= new SystemConfigState(_gameConfig, SystemConfigState.ConfigFps, 0);
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
        _uncapFrameRate?.Restore(_gameConfig);
        _uncapFrameRate = null;

        _limitFrameRateWhenClientInactive?.Restore(_gameConfig);
        _limitFrameRateWhenClientInactive = null;
    }

    public void Dispose()
    {
        _deliverooIpc.Dispose();
    }

    public uint GetRetainerItemCount(uint itemId) => _allaganToolsIpc.GetRetainerItemCount(itemId);

    private sealed record SystemConfigState(string Key, uint OldValue)
    {
        public const string ConfigFps = "Fps";
        public const string ConfigFpsInactive = "FPSInActive";

        public SystemConfigState(IGameConfig gameConfig, string key, uint newValue)
            : this(key, gameConfig.System.GetUInt(key))
        {
            gameConfig.System.Set(key, newValue);
        }

        public void Restore(IGameConfig gameConfig)
        {
            gameConfig.System.Set(Key, OldValue);
        }
    }
}
