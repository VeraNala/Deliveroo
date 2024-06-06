using Dalamud.Game.Text.SeStringHandling;

namespace Deliveroo;

internal interface IDeliveryResult;

internal sealed class MessageDeliveryResult : IDeliveryResult
{
    public SeString? Message { get; init; }
}

internal sealed class NoDeliveryResult : IDeliveryResult;
