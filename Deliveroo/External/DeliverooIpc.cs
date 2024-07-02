using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace Deliveroo.External;

internal sealed class DeliverooIpc : IDisposable
{
    private const string TurnInStarted = "Deliveroo.TurnInStarted";
    private const string TurnInStopped = "Deliveroo.TurnInStopped";
    private const string IsTurnInRunning = "Deliveroo.IsTurnInRunning";

    private readonly ICallGateProvider<bool> _isTurnInRunning;
    private readonly ICallGateProvider<object> _turnInStarted;
    private readonly ICallGateProvider<object> _turnInStopped;

    private bool _running;

    public DeliverooIpc(IDalamudPluginInterface pluginInterface)
    {
        _isTurnInRunning = pluginInterface.GetIpcProvider<bool>(IsTurnInRunning);
        _turnInStarted = pluginInterface.GetIpcProvider<object>(TurnInStarted);
        _turnInStopped = pluginInterface.GetIpcProvider<object>(TurnInStopped);

        _isTurnInRunning.RegisterFunc(CheckIsTurnInRunning);
    }

    public void StartTurnIn()
    {
        _running = true;
        _turnInStarted.SendMessage();
    }

    public void StopTurnIn()
    {
        _running = false;
        _turnInStopped.SendMessage();
    }

    private bool CheckIsTurnInRunning() => _running;

    public void Dispose()
    {
        _isTurnInRunning.UnregisterFunc();
    }
}
