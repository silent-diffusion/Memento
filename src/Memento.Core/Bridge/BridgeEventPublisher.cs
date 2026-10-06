using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge;

/// <summary>Serializes typed host events into <see cref="BridgeEventEnvelope{TPayload}"/> JSON and posts them to the UI.</summary>
public sealed class BridgeEventPublisher(IBridgeEventSink sink)
{
    public void PublishThemeChanged(ThemeChangedPayload payload) => sink.Post(SerializeThemeChanged(payload));

    public void PublishFooterStatus(FooterStatusPayload payload) => sink.Post(SerializeFooterStatus(payload));

    public static string SerializeThemeChanged(ThemeChangedPayload payload) =>
        JsonSerializer.Serialize(
            new BridgeEventEnvelope<ThemeChangedPayload>(BridgeEventNames.ThemeChanged, payload),
            BridgeJsonContext.Default.BridgeEventEnvelopeThemeChangedPayload);

    public static string SerializeFooterStatus(FooterStatusPayload payload) =>
        JsonSerializer.Serialize(
            new BridgeEventEnvelope<FooterStatusPayload>(BridgeEventNames.FooterStatus, payload),
            BridgeJsonContext.Default.BridgeEventEnvelopeFooterStatusPayload);
}
