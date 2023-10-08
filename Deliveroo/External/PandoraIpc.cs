using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Deliveroo.External;

internal sealed class PandoraIpc
{
    private const string GcTabFeature = "GCVendorDefault";

    private readonly DalamudReflector _dalamudReflector;

    public PandoraIpc(DalamudReflector dalamudReflector)
    {
        _dalamudReflector = dalamudReflector;
    }

    private IEnumerable<object>? GetFeatures()
    {
        if (_dalamudReflector.TryGetDalamudPlugin("Pandora's Box", out var plugin))
        {
            return ((IEnumerable)plugin!.GetType().GetProperty("Features")!.GetValue(plugin)!).Cast<object>();
        }

        return null;
    }

    private object? GetFeature(string name)
    {
        IEnumerable<object> features = GetFeatures() ?? Array.Empty<object>();
        return features.FirstOrDefault(x => x.GetType().Name == name);
    }

    public bool? DisableIfNecessary()
    {
        object? feature = GetFeature(GcTabFeature);
        if (feature == null)
            return null;

        if ((bool)feature.GetType().GetProperty("Enabled")!.GetValue(feature)!)
        {
            feature.GetType().GetMethod("Disable")!.Invoke(feature, Array.Empty<object>());
            return true;
        }

        return false;
    }

    public void Enable()
    {
        object? feature = GetFeature(GcTabFeature);
        if (feature == null)
            return;

        feature.GetType().GetMethod("Enable")!.Invoke(feature, Array.Empty<object>());
    }
}
