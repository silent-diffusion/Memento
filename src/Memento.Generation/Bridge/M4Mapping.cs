using System.Text.Json;
using Memento.AI.Payload;
using Memento.Core.Bridge.Contracts;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Model.Records;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Generation.Ai;
using BridgeTemplate = Memento.Core.Bridge.Contracts.Template;

namespace Memento.Generation.Bridge;

/// <summary>
/// Between the bridge records (BRIDGE.md M4, written by hand beside ui/src/bridge/types.ts) and Memento.Documents' model:
/// templates, styles, the module catalog, documents and generation records. Unknown enum values are refused with the
/// field named; nothing is guessed.
/// </summary>
public static class M4Mapping
{
    public static ModuleInfo ToInfo(ModuleDefinition module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var rules = module.GroundingRules.Select(GroundingRules.Find).OfType<GroundingRule>().ToList();
        return new ModuleInfo(
            module.Id,
            module.DisplayName,
            Name(module.Group),
            Name(module.Shape),
            module.IsAiGenerated,
            Name(module.DefaultLength),
            rules.Count == 0 ? null : string.Join(" ", rules.Select(r => r.Description)),
            module.DefaultInstructions)
        {
            GroundingRules = module.GroundingRules,
            DefaultLinkToTranscript = module.DefaultLinkToTranscript,
            Columns = module.Columns,
            Labels = module.Labels,
        };
    }

    public static ProviderInfo ToInfo(ProviderStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new ProviderInfo(status.Id, status.Name, ProviderIds.Vendor(status.Id), status.IsCloud ? "cloud" : "local", status.Ready, status.Reason, status.ModelLabel)
        {
            Code = status.Code,
            Detail = status.Detail,
            ModelId = status.IsCloud ? null : status.Model,
        };
    }

    public static BridgeTemplate ToBridge(DocumentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return new BridgeTemplate(
            template.Id,
            template.Name,
            template.BuiltIn,
            template.RecordingTypes,
            template.Rows.Select(r => new TemplateLayoutRow(r.Modules.Select(ToBridge).ToList())).ToList(),
            new InputSelection(template.Inputs.Transcript, template.Inputs.Details, template.Inputs.Participants, template.Inputs.Agenda, template.Inputs.Highlights, template.Inputs.ImportedDocuments, template.Inputs.PreviousDocuments),
            template.ProviderId,
            template.DefaultStyleId,
            new TemplateOutputSettings(template.Output.AlsoExportDocx, template.Output.AlsoExportMarkdown) { AlsoExportPdf = template.Output.AlsoExportPdf },
            template.ModifiedAt)
        {
            DocumentKind = string.IsNullOrWhiteSpace(template.DocumentKind) ? template.Name : template.DocumentKind,
            ProcessingInstructions = template.ProcessingInstructions,
            Customized = template.Customized,
        };
    }

    public static ModuleSettings ToBridge(TemplateModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return new ModuleSettings(module.Id, module.Type, module.Instructions, Name(module.Length), module.TextSize.Name(), module.LinkToTranscript, module.Title, module.CustomText);
    }

    /// <summary>The bridge template as a stored one; fields the bridge does not carry come from <paramref name="existing"/>.</summary>
    /// <exception cref="Core.Bridge.BridgeException"><c>bridge.invalidParams</c> naming the field.</exception>
    public static DocumentTemplate FromBridge(BridgeTemplate template, DocumentTemplate? existing, ModuleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(catalog);
        if (template.ProviderId is { } provider && !ProviderIds.IsValid(provider))
        {
            throw M4Errors.Invalid($"providerId: '{provider}' is not a provider. Use anthropic, openai, local or null.", "providerId");
        }

        if (string.IsNullOrWhiteSpace(template.Name))
        {
            throw M4Errors.Invalid("A template needs a name.", "name");
        }

        var inputs = template.Inputs ?? new InputSelection(true, true, true, true, true, false, false);
        var rows = (template.Rows ?? []).Select((row, r) => new TemplateRow
        {
            Modules = (row.Modules ?? []).Select((m, i) => FromBridge(m, existing?.Modules().FirstOrDefault(x => x.Id == m.Id), r, i)).ToList(),
        }).ToList();
        var output = template.Output ?? new TemplateOutputSettings(false, false);
        var result = (existing ?? new DocumentTemplate()) with
        {
            Name = template.Name.Trim(),
            DocumentKind = template.DocumentKind?.Trim() is { Length: > 0 } kind ? kind : existing?.DocumentKind is { Length: > 0 } kept ? kept : template.Name.Trim(),
            RecordingTypes = template.RecordingTypes ?? [],
            Rows = rows,
            Inputs = (existing?.Inputs ?? new TemplateInputs()) with
            {
                Transcript = inputs.Transcript,
                Details = inputs.Details,
                Participants = inputs.Participants,
                Agenda = inputs.Agenda,
                Highlights = inputs.Highlights,
                ImportedDocuments = inputs.Attachments,
                PreviousDocuments = inputs.PreviousDocuments,
            },
            ProviderId = template.ProviderId,
            DefaultStyleId = string.IsNullOrWhiteSpace(template.StyleId) ? existing?.DefaultStyleId ?? BuiltInStyles.CorporateId : template.StyleId,
            ProcessingInstructions = template.ProcessingInstructions ?? existing?.ProcessingInstructions ?? string.Empty,
            Output = (existing?.Output ?? new TemplateOutput()) with
            {
                AlsoExportDocx = output.AlsoExportDocx,
                AlsoExportMarkdown = output.AlsoExportMarkdown,
                AlsoExportPdf = output.AlsoExportPdf,
            },
        };
        var problems = result.Validate(catalog);
        if (problems.Count > 0)
        {
            throw M4Errors.Invalid(problems[0] + " Nothing was saved.", "rows");
        }

        return result;
    }

    public static TemplateModule FromBridge(ModuleSettings module, TemplateModule? existing, int row, int index)
    {
        ArgumentNullException.ThrowIfNull(module);
        var field = $"rows[{row}].modules[{index}]";
        return (existing ?? new TemplateModule()) with
        {
            Id = string.IsNullOrWhiteSpace(module.Id) ? $"m{row + 1}{index + 1}" : module.Id.Trim(),
            Type = module.Module,
            Title = string.IsNullOrWhiteSpace(module.CustomTitle) ? null : module.CustomTitle.Trim(),
            Instructions = module.Instructions ?? string.Empty,
            Length = Parse(module.Length, field + ".length", ModuleLength.Medium, ("short", ModuleLength.Short), ("medium", ModuleLength.Medium), ("long", ModuleLength.Long)),
            TextSize = Parse(module.TextSize, field + ".textSize", TextSize.Normal, ("smaller", TextSize.Smaller), ("normal", TextSize.Normal), ("larger", TextSize.Larger)),
            LinkToTranscript = module.LinkToTranscript,
            CustomText = module.CustomText,
        };
    }

    public static Style ToBridge(DocumentStyle style, int usedByTemplates)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new Style(style.Id, style.Name, style.BuiltIn, ToSettings(style), usedByTemplates, style.ModifiedAt) { Customized = style.Customized };
    }

    public static StyleSettings ToSettings(DocumentStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new StyleSettings(
            Name(style.HeadingTypeface),
            Name(style.BodyTypeface),
            Name(style.BaseSize),
            Name(style.HeadingCase),
            style.NumberedHeadings,
            Name(style.HeadingColor),
            style.TableHeaderFill,
            style.RuleUnderTitle,
            style.LinesBetweenSections,
            Name(style.Spacing),
            style.Paper == PaperSize.A4 ? "a4" : "letter",
            style.PageNumbers,
            style.RunningHeader);
    }

    /// <exception cref="Core.Bridge.BridgeException"><c>bridge.invalidParams</c> naming the field.</exception>
    public static DocumentStyle FromSettings(StyleSettings settings, DocumentStyle existing)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(existing);
        return existing with
        {
            HeadingTypeface = Parse(settings.HeadingFace, "settings.headingFace", Typeface.Sans, ("sans", Typeface.Sans), ("serif", Typeface.Serif)),
            BodyTypeface = Parse(settings.BodyFace, "settings.bodyFace", Typeface.Sans, ("sans", Typeface.Sans), ("serif", Typeface.Serif)),
            BaseSize = Parse(settings.BaseSize, "settings.baseSize", BaseSize.Normal, ("small", BaseSize.Small), ("normal", BaseSize.Normal), ("large", BaseSize.Large)),
            HeadingCase = Parse(settings.HeadingCase, "settings.headingCase", HeadingCase.Normal, ("normal", HeadingCase.Normal), ("smallCaps", HeadingCase.SmallCaps)),
            NumberedHeadings = settings.NumberedHeadings,
            HeadingColor = Parse(settings.HeadingColor, "settings.headingColor", HeadingColor.Ink, ("navy", HeadingColor.Navy), ("ink", HeadingColor.Ink), ("forest", HeadingColor.Forest), ("burgundy", HeadingColor.Burgundy)),
            TableHeaderFill = settings.TableHeaderFill,
            RuleUnderTitle = settings.RuleUnderTitle,
            LinesBetweenSections = settings.LinesBetweenSections,
            Spacing = Parse(settings.Spacing, "settings.spacing", Spacing.Normal, ("tight", Spacing.Tight), ("normal", Spacing.Normal), ("airy", Spacing.Airy)),
            Paper = Parse(settings.Paper, "settings.paper", PaperSize.Letter, ("letter", PaperSize.Letter), ("a4", PaperSize.A4)),
            PageNumbers = settings.PageNumbers,
            RunningHeader = settings.RunningHeader,
        };
    }

    /// <summary>The ticks of a template as the payload selection (chapters travel with the transcript; instructions always do).</summary>
    public static PayloadSelection Selection(TemplateInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        return new PayloadSelection
        {
            Transcript = inputs.Transcript,
            Details = inputs.Details,
            Participants = inputs.Participants,
            Agenda = inputs.Agenda,
            ChaptersAndTopics = inputs.Transcript,
            Highlights = inputs.Highlights,
            Attachments = inputs.ImportedDocuments,
            PreviousDocuments = inputs.PreviousDocuments,
            Instructions = true,
        };
    }

    public static InputSelection ToBridge(PayloadSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new InputSelection(selection.Transcript, selection.Details, selection.Participants, selection.Agenda, selection.Highlights, selection.Attachments, selection.PreviousDocuments);
    }

    public static RecordInputs ToRecord(PayloadSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return new RecordInputs
        {
            Transcript = selection.Transcript,
            Details = selection.Details,
            Participants = selection.Participants,
            Agenda = selection.Agenda,
            Highlights = selection.Highlights,
            Attachments = selection.Attachments,
            PreviousDocuments = selection.PreviousDocuments,
        };
    }

    public static GenerationRecord? ToBridge(DocumentGenerationRecord? record)
    {
        if (record is null)
        {
            return null;
        }

        var inputs = record.Inputs;
        return new GenerationRecord(
            record.TemplateId,
            record.TemplateName,
            record.StyleId,
            record.ProviderId,
            record.ModelLabel,
            record.StartedAt,
            record.DurationMs,
            new InputSelection(inputs.Transcript, inputs.Details, inputs.Participants, inputs.Agenda, inputs.Highlights, inputs.Attachments, inputs.PreviousDocuments),
            record.PayloadHash,
            record.PayloadText is not null,
            record.Chunks,
            record.Modules.Where(m => m.Source == "ai").Select(m => new GenerationModuleRecord(m.ModuleId, m.Claims, m.Verified, m.Dropped, m.NotDiscussed)).ToList())
        {
            Sent = record.Sent,
            Bytes = record.Bytes,
            StayedOnPc = record.StayedOnPc,
            Claims = record.Claims.Select(c => new GenerationClaim(c.Id, c.ModuleId, c.Text, c.T, c.Verdict, c.Kept, c.Note)).ToList(),
            PayloadText = record.PayloadText,
        };
    }

    public static DocumentContent ToContent(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Rows of modules { id, module, title, textSize, linkToTranscript, blocks }, the stored model with "type" named "module".
        var stored = JsonSerializer.SerializeToNode(document, DocumentJsonContext.Default.Document)!;
        var rows = new System.Text.Json.Nodes.JsonArray();
        foreach (var row in stored["rows"]?.AsArray() ?? [])
        {
            var modules = new System.Text.Json.Nodes.JsonArray();
            foreach (var module in row?["modules"]?.AsArray() ?? [])
            {
                modules.Add(new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = module?["id"]?.DeepClone(),
                    ["module"] = module?["type"]?.DeepClone(),
                    ["title"] = module?["title"]?.DeepClone(),
                    ["textSize"] = module?["textSize"]?.DeepClone() ?? "normal",
                    ["linkToTranscript"] = module?["linkToTranscript"]?.DeepClone() ?? false,
                    ["blocks"] = module?["blocks"]?.DeepClone() ?? new System.Text.Json.Nodes.JsonArray(),
                });
            }

            rows.Add(new System.Text.Json.Nodes.JsonObject { ["modules"] = modules });
        }

        return new DocumentContent(document.SchemaVersion, document.Id, document.Title, MetaLine.Format(document.Meta), JsonSerializer.SerializeToElement(rows, M4NodeJsonContext.Default.JsonArray), ToBridge(document.Record))
        {
            StyleId = document.StyleId,
            Version = document.Version,
        };
    }

    public static DocumentSummary ToSummary(Document document, int versions, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new DocumentSummary(
            document.Id,
            document.Name ?? document.Title,
            document.Generation is null ? "written" : "generated",
            document.Record?.TemplateName,
            document.StyleId ?? BuiltInStyles.CorporateId,
            document.Generation?.ProviderId,
            document.Generation?.GeneratedAt,
            document.Version,
            versions,
            document.ModifiedAt,
            sizeBytes);
    }

    public static string Name<TEnum>(TEnum value)
        where TEnum : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());

    private static T Parse<T>(string? value, string field, T fallback, params (string Name, T Value)[] choices)
    {
        if (value is null)
        {
            return fallback;
        }

        foreach (var (name, choice) in choices)
        {
            if (string.Equals(name, value, StringComparison.Ordinal))
            {
                return choice;
            }
        }

        throw M4Errors.Invalid($"{field}: '{value}' is not available. Choose {string.Join(", ", choices.Select(c => c.Name))}.", field);
    }
}
