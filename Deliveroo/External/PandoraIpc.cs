using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace Deliveroo.External;

internal sealed class PandoraIpc
{
    private const string GcTabFeature = "Default Grand Company Shop Menu";

    private readonly IPluginLog _pluginLog;
    private readonly ICallGateSubscriber<string, bool?> _getEnabled;
    private readonly ICallGateSubscriber<string, bool, object?> _setEnabled;

    public PandoraIpc(DalamudPluginInterface pluginInterface, IPluginLog pluginLog)
    {
        _pluginLog = pluginLog;
        _getEnabled = pluginInterface.GetIpcSubscriber<string, bool?>("PandorasBox.GetFeatureEnabled");
        _setEnabled = pluginInterface.GetIpcSubscriber<string, bool, object?>("PandorasBox.SetFeatureEnabled");
    }

    public bool? DisableIfNecessary()
    {
        try
        {
            bool? enabled = _getEnabled.InvokeFunc(GcTabFeature);
            _pluginLog.Information($"Pandora's {GcTabFeature} is {enabled?.ToString() ?? "null"}");
            if (enabled == true)
                _setEnabled.InvokeAction(GcTabFeature, false);

            return enabled;
        }
        catch (IpcNotReadyError e)
        {
            _pluginLog.Information(e, "Unable to read pandora state");
            return null;
        }
    }

    public void Enable()
    {
        try
        {
            _setEnabled.InvokeAction(GcTabFeature, true);
        }
        catch (IpcNotReadyError e)
        {
            _pluginLog.Error(e, "Unable to restore pandora state");
        }
    }
}
