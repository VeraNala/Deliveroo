using System.Reflection;
using LLib;

namespace Deliveroo.External;

internal sealed class YesAlreadyIpc
{
    private readonly DalamudReflector _dalamudReflector;

    public YesAlreadyIpc(DalamudReflector dalamudReflector)
    {
        _dalamudReflector = dalamudReflector;
    }

    private object? GetConfiguration()
    {
        if (_dalamudReflector.TryGetDalamudPlugin("Yes Already", out var plugin))
        {
            var pluginService = plugin!.GetType().Assembly.GetType("YesAlready.Service");
            return pluginService!.GetProperty("Configuration", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        }

        return null;
    }

    public bool? DisableIfNecessary()
    {
        object? configuration = GetConfiguration();
        if (configuration == null)
            return null;

        var property = configuration.GetType().GetProperty("Enabled")!;
        bool enabled = (bool)property.GetValue(configuration)!;
        if (enabled)
        {
            property.SetValue(configuration, false);
            return true;
        }

        return false;
    }

    public void Enable()
    {
        object? configuration = GetConfiguration();
        if (configuration == null)
            return;


        var property = configuration.GetType().GetProperty("Enabled")!;
        property.SetValue(configuration, true);
    }
}
