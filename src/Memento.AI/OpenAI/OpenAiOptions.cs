using Memento.AI.Http;

namespace Memento.AI.OpenAI;

/// <summary>Settings for <see cref="OpenAiProvider"/>; the defaults are the production values.</summary>
public sealed record OpenAiOptions
{
    public static Uri DefaultBaseUrl { get; } = new("https://api.openai.com/");

    /// <summary>The API root; tests point it at a local fake server. Must end with a slash.</summary>
    public Uri BaseUrl { get; init; } = DefaultBaseUrl;

    /// <summary>OpenAI's most capable model as listed in its model documentation (October 2026).</summary>
    public string Model { get; init; } = Memento.Core.Ai.AiModelDefaults.OpenAiModel;

    /// <summary><c>reasoning.effort</c> (<c>low</c> … <c>max</c>), or <c>null</c> for the model default.</summary>
    public string? ReasoningEffort { get; init; } = "high";

    /// <summary>Reasoning models reject sampling parameters; enable only for a model that accepts <c>temperature</c>.</summary>
    public bool SendTemperature { get; init; }

    public int ContextTokens { get; init; } = 1_050_000;

    public int MaxOutputTokens { get; init; } = 128_000;

    public CloudHttpOptions Http { get; init; } = new();

    /// <summary>
    /// The options for a model from Settings (<see cref="Memento.Core.Ai.AiModelDefaults.Describe"/>): its context and
    /// answer limit, and <c>reasoning.effort</c> only when the model is in Memento's list (a model typed by hand may not reason).
    /// </summary>
    public static OpenAiOptions For(Memento.Core.Ai.CloudModel model, Uri? baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var defaults = new OpenAiOptions();
        return defaults with
        {
            BaseUrl = baseUrl ?? DefaultBaseUrl,
            Model = model.Id,
            ReasoningEffort = model.SendsEffort ? defaults.ReasoningEffort : null,
            ContextTokens = model.ContextTokens,
            MaxOutputTokens = model.MaxOutputTokens,
        };
    }
}
