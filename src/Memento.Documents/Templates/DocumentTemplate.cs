using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;

namespace Memento.Documents.Templates;

/// <summary>
/// A saved document template (PRODUCT-SPEC "AI Templates", ARCHITECTURE.md §8, schema v1): which inputs the AI receives,
/// which modules are generated in which rows, per-module settings, the provider, the default style and the output options.
/// </summary>
public sealed record DocumentTemplate
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>What the generated document is called in its meta line and Generate button ("Meeting minutes").</summary>
    public string DocumentKind { get; init; } = string.Empty;

    /// <summary>The recording types the template is offered for by default (BRIDGE.md <c>RecordingType</c>); empty means all.</summary>
    public IReadOnlyList<string> RecordingTypes { get; init; } = [];

    public IReadOnlyList<TemplateRow> Rows { get; init; } = [];

    public TemplateInputs Inputs { get; init; } = new();

    /// <summary>The provider to use (<c>anthropic</c>, <c>openai</c>, <c>local</c>); <c>null</c> uses the default from Settings.</summary>
    public string? ProviderId { get; init; }

    public string DefaultStyleId { get; init; } = "corporate";

    /// <summary>Optional instructions that apply to the whole document; they can never override the grounding rules.</summary>
    public string ProcessingInstructions { get; init; } = string.Empty;

    public TemplateOutput Output { get; init; } = new();

    /// <summary>Set by the store: one of the templates that ship with Memento.</summary>
    public bool BuiltIn { get; init; }

    /// <summary>Set by the store: a built-in template the user has changed (Reset is available). Not written to disk.</summary>
    [JsonIgnore]
    public bool Customized { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public IEnumerable<TemplateModule> Modules() => Rows.SelectMany(r => r.Modules);

    /// <summary>Structural problems as sentences: rows of one to three modules, unique ids, modules the catalog knows.</summary>
    public IReadOnlyList<string> Validate(ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var problems = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var r = 0; r < Rows.Count; r++)
        {
            var count = Rows[r].Modules.Count;
            if (count is < 1 or > DocumentRow.MaxModules)
            {
                problems.Add(FormattableString.Invariant($"Row {r + 1} holds {count} modules; a row holds one to three."));
            }

            foreach (var module in Rows[r].Modules)
            {
                if (string.IsNullOrWhiteSpace(module.Id) || !ids.Add(module.Id))
                {
                    problems.Add(FormattableString.Invariant($"Row {r + 1} has a module without a unique id (\"{module.Id}\")."));
                }

                if (catalog.Find(module.Type) is null)
                {
                    problems.Add($"The module type \"{module.Type}\" is not in the catalog.");
                }
            }
        }

        return problems;
    }
}
