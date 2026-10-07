using System.Text.Json;
using Memento.Core.Ai;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// The M4 settings (BRIDGE.md "Settings (M4)"): <c>ai.defaultProviderId</c>, <c>ai.localModelId</c>, the model of each
/// cloud provider, and <c>documents.defaultTemplateId</c> / <c>defaultStyleId</c>. Each field present in
/// <c>settings.set</c> is changed (a JSON <c>null</c> clears it); every value is checked before anything is written.
/// </summary>
internal static class M4SettingsBlocks
{
    public const string Anthropic = "anthropic";
    public const string OpenAi = "openai";
    public const string Local = "local";

    /// <summary>The template and style a new document starts from when Settings names none (the built-ins).</summary>
    public const string BuiltInTemplateId = "meeting-minutes";
    public const string BuiltInStyleId = "corporate";

    public static IReadOnlyList<string> ProviderIds { get; } = [Anthropic, OpenAi, Local];

    /// <summary>Adds the M4 fields to a snapshot.</summary>
    public static SettingsSnapshot Complete(SettingsSnapshot snapshot, AppSettings settings, EngineSelector selector)
    {
        var ai = settings.Ai;
        var localModel = LocalModelChoice.EffectiveId(ai.LocalModelId, selector.Catalog, selector.Sample());
        return snapshot with
        {
            Ai = snapshot.Ai with
            {
                DefaultProviderId = ai.DefaultProviderId,
                LocalModelId = localModel,
                LocalModelChosen = ai.LocalModelId is not null && localModel == ai.LocalModelId,
                Providers = new AiProvidersSnapshot(
                    snapshot.Ai.Providers.Anthropic with { Model = ai.Providers.Anthropic.Model ?? AiModelDefaults.AnthropicModel, Models = AiModelDefaults.AnthropicModels },
                    snapshot.Ai.Providers.Openai with { Model = ai.Providers.Openai.Model ?? AiModelDefaults.OpenAiModel, Models = AiModelDefaults.OpenAiModels }),
            },
            Documents = new DocumentsSettingsSnapshot(settings.Documents.DefaultTemplateId ?? BuiltInTemplateId, settings.Documents.DefaultStyleId ?? BuiltInStyleId),
        };
    }

    /// <summary>Checks the M4 fields of <paramref name="parameters"/>, then applies them to <paramref name="current"/>.</summary>
    /// <exception cref="BridgeException"><c>settings.invalidValue</c> naming the field.</exception>
    public static AppSettings Merge(AppSettings current, SettingsSetParams parameters, IModelManager models)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(parameters);
        var next = current;
        if (parameters.Ai is { } ai)
        {
            var settings = next.Ai;
            var defaultProvider = Choice(ai.DefaultProviderId, "ai.defaultProviderId", settings.DefaultProviderId, id => ProviderIds.Contains(id, StringComparer.Ordinal)
                ? null
                : $"ai.defaultProviderId: '{id}' is not a provider. Choose anthropic, openai or local. Nothing was changed.");
            var localModel = Choice(ai.LocalModelId, "ai.localModelId", settings.LocalModelId, id =>
            {
                var entry = models.Catalog.Find(id);
                if (entry is not { Kind: ModelKinds.Llm })
                {
                    return $"ai.localModelId: there is no local model called '{id}'. Choose one of the local models listed in Settings › AI and privacy. Nothing was changed.";
                }

                return models.IsInstalled(id) || id == settings.LocalModelId
                    ? null
                    : $"ai.localModelId: {entry.Name} is not installed, so it can't be chosen yet. Download it in Settings › AI and privacy first. Nothing was changed.";
            });
            var anthropic = Choice(ai.Providers?.Anthropic?.Model ?? default, "ai.providers.anthropic.model", settings.Providers.Anthropic.Model, ModelProblem("ai.providers.anthropic.model"));
            var openai = Choice(ai.Providers?.Openai?.Model ?? default, "ai.providers.openai.model", settings.Providers.Openai.Model, ModelProblem("ai.providers.openai.model"));
            next = next with
            {
                Ai = next.Ai with
                {
                    DefaultProviderId = defaultProvider,
                    LocalModelId = localModel,
                    Providers = next.Ai.Providers with
                    {
                        Anthropic = next.Ai.Providers.Anthropic with { Model = anthropic },
                        Openai = next.Ai.Providers.Openai with { Model = openai },
                    },
                },
            };
        }

        if (parameters.Documents is { } documents)
        {
            next = next with
            {
                Documents = next.Documents with
                {
                    DefaultTemplateId = Choice(documents.DefaultTemplateId, "documents.defaultTemplateId", next.Documents.DefaultTemplateId, IdProblem("documents.defaultTemplateId", "template")),
                    DefaultStyleId = Choice(documents.DefaultStyleId, "documents.defaultStyleId", next.Documents.DefaultStyleId, IdProblem("documents.defaultStyleId", "style")),
                },
            };
        }

        return next;
    }

    /// <summary>Template and style ids: lower-case letters, digits and hyphens (they are file names).</summary>
    internal static bool IsStoreId(string id) =>
        id.Length is > 0 and <= 64
        && (char.IsAsciiLetterLower(id[0]) || char.IsAsciiDigit(id[0]))
        && id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    private static Func<string, string?> ModelProblem(string field) => id => AiModelDefaults.IsValidModelId(id)
        ? null
        : $"{field}: '{id}' is not a model id. Use letters, digits and . _ : / - (up to {AiModelDefaults.MaxModelIdLength} characters), or null for the default. Nothing was changed.";

    private static Func<string, string?> IdProblem(string field, string noun) => id => IsStoreId(id)
        ? null
        : $"{field}: '{id}' is not a {noun} id. Ids use lower-case letters, digits and hyphens. Nothing was changed.";

    /// <summary>Omitted keeps <paramref name="current"/>; <c>null</c> clears; a string is checked by <paramref name="problem"/>.</summary>
    private static string? Choice(JsonElement element, string field, string? current, Func<string, string?> problem)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Undefined:
                return current;
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.String:
                var value = element.GetString()!.Trim();
                if (problem(value) is { } message)
                {
                    throw new BridgeException(DomainErrorCodes.SettingsInvalidValue, message, field);
                }

                return value;
            default:
                throw new BridgeException(DomainErrorCodes.SettingsInvalidValue, $"{field} must be a string or null. Nothing was changed.", field);
        }
    }
}
