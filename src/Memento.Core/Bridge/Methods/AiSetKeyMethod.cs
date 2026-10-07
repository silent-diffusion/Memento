using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Secrets;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>ai.setKey</c>: stores the key with DPAPI; the answer only says that one is saved.</summary>
public sealed class AiSetKeyMethod(ISecretStore secrets) : BridgeMethod<AiSetKeyParams, AiKeyResult>
{
    public const int MinKeyLength = 8;
    public const int MaxKeyLength = 500;

    public override string Name => BridgeMethodNames.AiSetKey;

    public override JsonTypeInfo<AiSetKeyParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AiSetKeyParams;

    public override JsonTypeInfo<AiKeyResult> ResultTypeInfo => M3BridgeJsonContext.Default.AiKeyResult;

    public override async Task<AiKeyResult> InvokeAsync(AiSetKeyParams parameters, CancellationToken cancellationToken)
    {
        var provider = ValidateProvider(parameters.Provider);
        var key = (parameters.Key ?? string.Empty).Trim();

        // Never echo the key, not even part of it, in a message.
        if (key.Length is < MinKeyLength or > MaxKeyLength || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw M3Errors.Invalid($"That does not look like an API key: a key is {MinKeyLength} to {MaxKeyLength} characters with no spaces. Nothing was saved. Copy the whole key from the provider's console and paste it again.");
        }

        try
        {
            await secrets.SetKeyAsync(provider, key, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new BridgeException(
                DomainErrorCodes.AiKeyWriteFailed,
                $"The {AiProviders.DisplayName(provider)} key could not be saved: Windows did not let Memento open its key file ({ex.GetType().Name}); another program may be using it. Nothing was saved and any other saved key is unchanged. Try again in a moment.");
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            throw new BridgeException(
                DomainErrorCodes.AiKeyWriteFailed,
                $"Windows could not store the {AiProviders.DisplayName(provider)} key securely ({ex.GetType().Name}). Nothing was saved; no key is kept in plain text. Try again, or sign out of Windows and back in.");
        }

        return new AiKeyResult(secrets.HasKey(provider));
    }

    internal static string ValidateProvider(string? provider) =>
        AiProviders.IsValid(provider)
            ? provider!
            : throw M3Errors.Invalid($"Provider '{provider}' is not available. Choose {string.Join(" or ", AiProviders.All)}.");
}
