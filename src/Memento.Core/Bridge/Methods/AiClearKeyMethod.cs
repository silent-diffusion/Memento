using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Secrets;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>ai.clearKey</c>: removes the saved key.</summary>
public sealed class AiClearKeyMethod(ISecretStore secrets) : BridgeMethod<AiProviderParams, AiKeyResult>
{
    public override string Name => BridgeMethodNames.AiClearKey;

    public override JsonTypeInfo<AiProviderParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AiProviderParams;

    public override JsonTypeInfo<AiKeyResult> ResultTypeInfo => M3BridgeJsonContext.Default.AiKeyResult;

    public override async Task<AiKeyResult> InvokeAsync(AiProviderParams parameters, CancellationToken cancellationToken)
    {
        var provider = AiSetKeyMethod.ValidateProvider(parameters.Provider);
        try
        {
            await secrets.ClearKeyAsync(provider, cancellationToken);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            throw new BridgeException(
                DomainErrorCodes.AiKeyWriteFailed,
                $"The {AiProviders.DisplayName(provider)} key could not be removed ({ex.GetType().Name}). It is still saved, encrypted for your Windows account. Try again.");
        }

        return new AiKeyResult(secrets.HasKey(provider));
    }
}
