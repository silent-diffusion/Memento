using System.Globalization;
using System.Text.Json;
using Memento.Core.Ai;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Models;
using Memento.Core.Secrets;
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

    /// <summary>2.5.0: "Your own server".</summary>
    public const string Server = "server";

    /// <summary>The template and style a new document starts from when Settings names none (the built-ins).</summary>
    public const string BuiltInTemplateId = "meeting-minutes";
    public const string BuiltInStyleId = "corporate";

    public static IReadOnlyList<string> ProviderIds { get; } = [Anthropic, OpenAi, Server, Local];

    /// <summary>Adds the M4 fields to a snapshot.</summary>
    public static SettingsSnapshot Complete(SettingsSnapshot snapshot, AppSettings settings, EngineSelector selector)
    {
        var ai = settings.Ai;
        var localModel = selector.EffectiveLocalModelId(ai.LocalModelId);
        return snapshot with
        {
            Ai = snapshot.Ai with
            {
                DefaultProviderId = ai.DefaultProviderId,
                LocalModelId = localModel,
                LocalModelChosen = ai.LocalModelId is not null && localModel == ai.LocalModelId,
                Providers = new AiProvidersSnapshot(
                    WithModel(snapshot.Ai.Providers.Anthropic, AiProviders.Anthropic, ai.Providers.Anthropic.Model),
                    WithModel(snapshot.Ai.Providers.Openai, AiProviders.OpenAi, ai.Providers.Openai.Model))
                {
                    Server = snapshot.Ai.Providers.Server,
                },
            },
            Documents = new DocumentsSettingsSnapshot(settings.Documents.DefaultTemplateId ?? BuiltInTemplateId, settings.Documents.DefaultStyleId ?? BuiltInStyleId)
            {
                OcrEngine = EffectiveOcrEngine(settings.Documents.OcrEngine, selector.IsInstalled(OcrEngines.TesseractEnglishModelId)),
            },
        };
    }

    /// <summary>A cloud provider's model in effect, the models Settings lists with their notes, and whether it was typed by hand.</summary>
    internal static AiProviderKeyState WithModel(AiProviderKeyState state, string providerId, string? configured)
    {
        var model = AiModelDefaults.Describe(providerId, AiModelDefaults.Effective(providerId, configured));
        var catalog = AiModelDefaults.Catalog(providerId);
        return state with
        {
            Model = model.Id,
            Models = catalog.Select(m => m.Id).ToList(),
            Choices = catalog.Select(m => new AiModelChoice(m.Id, m.Note, m.ContextTokens)).ToList(),
            Custom = !model.Listed,
            ModelNote = model.Note,
        };
    }

    /// <summary>2.0: the text recognition in effect: Tesseract only while its language data is installed.</summary>
    public static string EffectiveOcrEngine(string? chosen, bool tesseractInstalled) =>
        chosen == OcrEngines.Tesseract && tesseractInstalled ? OcrEngines.Tesseract : OcrEngines.Windows;

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
                : $"ai.defaultProviderId: '{id}' is not a provider. Choose anthropic, openai, server or local. Nothing was changed.");
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
            var server = MergeServer(settings.Server, ai.Providers?.Server);
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
                    Server = server,
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
                    OcrEngine = Choice(documents.OcrEngine, "documents.ocrEngine", next.Documents.OcrEngine, id =>
                    {
                        if (!OcrEngines.IsValid(id))
                        {
                            return $"documents.ocrEngine: '{id}' is not a text recognition engine. Choose windows or tesseract. Nothing was changed.";
                        }

                        return id == OcrEngines.Tesseract && !models.IsInstalled(OcrEngines.TesseractEnglishModelId)
                            ? "documents.ocrEngine: Tesseract English is not installed, so it can't be chosen yet. Download it in Settings › Documents first. Nothing was changed."
                            : null;
                    }),
                },
            };
        }

        return next;
    }

    /// <summary>
    /// 2.5.0, <c>ai.providers.server</c>: the address is saved normalized (<see cref="ServerAddress.Text"/>), or cleared by
    /// <c>null</c>; the model as the server spells it, or cleared; the context clamped to 2,048…1,048,576 tokens, as the page clamps it. A bad value
    /// names its field and nothing is written. The address is never contacted here.
    /// </summary>
    private static AiServerSettings MergeServer(AiServerSettings current, AiServerPatch? patch)
    {
        if (patch is null)
        {
            return current;
        }

        const string AddressField = "ai.providers.server.address";
        const string ModelField = "ai.providers.server.model";
        var address = Choice(patch.Address, AddressField, current.Address, text => ServerAddress.Parse(text, out var problem) is null
            ? $"{AddressField}: {Shown(text)} is not a server address: {problem}. Enter it as http://localhost:11434 or https://server.example. Nothing was changed."
            : null);
        if (address is not null)
        {
            address = ServerAddress.Parse(address, out _)!.Text;
        }

        var model = Choice(patch.Model, ModelField, current.Model, id => AiServerSettings.IsValidModel(id)
            ? null
            : $"{ModelField}: {Shown(id)} is not a model name. Use the name the server lists (letters, digits and . _ : / - @ +, up to {AiServerSettings.MaxModelLength} characters), or null for none. Nothing was changed.");
        var context = patch.ContextTokens is { } tokens ? AiServerSettings.ClampContext(tokens) : current.ContextTokens;

        return current with { Address = address, Model = model, ContextTokens = context };
    }

    /// <summary>A refused value as a message shows it: quoted, at most 60 characters, and never an address with a user name or password in it.</summary>
    private static string Shown(string value)
    {
        var scheme = value.IndexOf("://", StringComparison.Ordinal);
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0 && (scheme < 0 || at > scheme) && value.IndexOf('/', Math.Max(0, scheme + 3)) is var slash && (slash < 0 || at < slash))
        {
            return "an address with a user name";
        }

        return "'" + (value.Length <= 60 ? value : value[..57] + "...") + "'";
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
