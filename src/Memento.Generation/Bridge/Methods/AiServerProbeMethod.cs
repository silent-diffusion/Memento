using System.Text.Json.Serialization.Metadata;
using Memento.AI;
using Memento.AI.Http;
using Memento.AI.Server;
using Memento.Core.Ai;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Secrets;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Memento.Generation.Bridge.Methods;

/// <summary>
/// <c>ai.serverProbe</c> (2.5.0): "Test connection" for "Your own server". The only request Memento sends without a
/// generation, and only when the page asks: <c>GET /v1/models</c> (and Ollama's <c>/api/version</c>) with the saved key
/// if any, through the shared client (no redirects). An address that is not this PC is contacted only while "Allow
/// external AI services" is on (<c>ai.disabled</c> otherwise), and a plain <c>http://</c> address outside this PC and
/// the local network never (<c>ai.serverInsecure</c>). The address need not be saved yet; nothing is saved here.
/// </summary>
public sealed class AiServerProbeMethod(Lazy<AiHttpClient> http, ISecretReader secrets, ISettingsStore settings, TimeProvider time, ILoggerFactory loggers)
    : M4Method<AiServerProbeParams, AiServerProbeResult>
{
    public override string Name => BridgeMethodNames.AiServerProbe;

    public override JsonTypeInfo<AiServerProbeParams> ParamsTypeInfo => M4BridgeJsonContext.Default.AiServerProbeParams;

    public override JsonTypeInfo<AiServerProbeResult> ResultTypeInfo => M4BridgeJsonContext.Default.AiServerProbeResult;

    public override async Task<AiServerProbeResult> InvokeAsync(AiServerProbeParams parameters, CancellationToken cancellationToken)
    {
        if (ServerAddress.Parse(parameters.Address, out var problem) is not { } address)
        {
            throw new BridgeException(
                DomainErrorCodes.SettingsInvalidValue,
                $"That is not a server address: {problem}. Enter it as http://localhost:11434 or https://server.example. Nothing was contacted.",
                "address");
        }

        AiServerProbeResult Result(bool reachable, string? name, IReadOnlyList<string> models, string? code, string detail) =>
            new(reachable, name, models, code, detail, address.IsLoopback, address.LeavesPc, address.IsInsecure);

        if (address.IsInsecure)
        {
            return Result(false, null, [], DomainErrorCodes.AiServerInsecure, $"{address.Text} is outside this PC and your local network, and Memento sends there only over https, so it did not contact it. Use an https:// address.");
        }

        if (address.LeavesPc && !settings.Current.Ai.Enabled)
        {
            return Result(false, null, [], DomainErrorCodes.AiDisabled, $"The server at {address.Text} is not on this PC, and external AI is off, so Memento did not contact it. Turn on \"Allow external AI services\" in Settings › AI and privacy to test it, or use a server on this PC.");
        }

        var key = secrets.HasKey(AiProviders.Server) ? await secrets.GetKeyAsync(AiProviders.Server, cancellationToken) : null;
        var probe = new ServerProbe(http.Value.Client, time, loggers.CreateLogger<ServerProbe>());
        var outcome = await probe.ProbeAsync(address, key, cancellationToken);
        return Result(outcome.Reachable, outcome.ServerName, outcome.Models, outcome.Code, outcome.Detail);
    }
}
