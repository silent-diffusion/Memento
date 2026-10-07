using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Tests.M4;

/// <summary>Pins the JSON of the M4 events and records, and the requests the UI sends (BRIDGE.md M4).</summary>
public sealed class M4ContractSerializationTests
{
    private static readonly InputSelection Inputs = new(true, true, true, true, true, false, false);

    /// <summary>The bridge escapes the middle dot of meta lines and labels (<c>·</c>); the expectations show it plainly.</summary>
    private static string Dot(string json) => json.Replace("\\u00B7", "·", StringComparison.Ordinal);

    [Fact]
    public void GenerationProgressEvent()
    {
        Assert.Equal(
            """{"event":"generation.progress","payload":{"jobId":"g1","recordingId":"r1","documentId":null,"stage":"generating","moduleId":"m07","percent":42.5,"message":"Reading the transcript","code":null}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.GenerationProgress, new GenerationProgress("g1", "r1", null, "generating", "m07", 42.5, "Reading the transcript"), M4BridgeJsonContext.Default.BridgeEventEnvelopeGenerationProgress));
        Assert.Equal(
            """{"event":"generation.progress","payload":{"jobId":"g1","recordingId":"r1","documentId":"d1","stage":"failed","moduleId":null,"percent":0,"message":"Claude could not be reached: no network.","code":"ai.network"}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.GenerationProgress, new GenerationProgress("g1", "r1", "d1", "failed", null, 0, "Claude could not be reached: no network.") { Code = "ai.network" }, M4BridgeJsonContext.Default.BridgeEventEnvelopeGenerationProgress));
    }

    [Fact]
    public void DocumentsTemplatesAndStylesChangedEvents()
    {
        Assert.Equal(
            """{"event":"documents.changed","payload":{"recordingId":"r1","documentId":"d1","reason":"generated"}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.DocumentsChanged, new DocumentsChangedPayload("r1", "d1", "generated"), M4BridgeJsonContext.Default.BridgeEventEnvelopeDocumentsChangedPayload));
        Assert.Equal(
            """{"event":"templates.changed","payload":{}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.TemplatesChanged, new EmptyPayload(), M4BridgeJsonContext.Default.BridgeEventEnvelopeEmptyPayload));
        Assert.Equal(
            """{"event":"styles.changed","payload":{}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.StylesChanged, new EmptyPayload(), M4BridgeJsonContext.Default.BridgeEventEnvelopeEmptyPayload));
    }

    [Fact]
    public void ProvidersAndTheSendSummary()
    {
        var providers = new ProvidersListResult(
            [new ProviderInfo("local", "Local model", "This PC", "local", true, null, "Qwen3.5 4B · graphics card") { Detail = "Runs on the graphics card with a 16k context. Nothing leaves this PC.", ModelId = "qwen3.5-4b-q4" }],
            false);
        Assert.Equal(
            """{"providers":[{"id":"local","name":"Local model","vendor":"This PC","kind":"local","ready":true,"reason":null,"modelLabel":"Qwen3.5 4B · graphics card","code":null,"detail":"Runs on the graphics card with a 16k context. Nothing leaves this PC.","modelId":"qwen3.5-4b-q4"}],"externalAiEnabled":false,"defaultProviderId":null}""",
            Dot(JsonSerializer.Serialize(providers, M4BridgeJsonContext.Default.ProvidersListResult)));

        var start = new GenerationStartResult("g1") { ConfirmationRequired = true, Summary = new GenerationSendSummary("anthropic", "Claude", "claude-opus-5-5", Inputs, 18432, 1) };
        Assert.Equal(
            """{"jobId":"g1","confirmationRequired":true,"summary":{"providerId":"anthropic","providerName":"Claude","modelLabel":"claude-opus-5-5","inputsUsed":{"transcript":true,"details":true,"participants":true,"agenda":true,"highlights":true,"attachments":false,"previousDocuments":false},"bytes":18432,"chunks":1}}""",
            JsonSerializer.Serialize(start, M4BridgeJsonContext.Default.GenerationStartResult));
    }

    [Fact]
    public void DocumentsAndVersions()
    {
        using var rows = JsonDocument.Parse("""[{"modules":[{"id":"m01","module":"executiveSummary","title":"Executive summary","textSize":"larger","linkToTranscript":true,"blocks":[]}]}]""");
        var content = new DocumentContent(1, "d1", "Weekly sync", "Meeting minutes · Monday 5 October 2026, 4:00 PM", rows.RootElement.Clone(), null) { StyleId = "corporate", Version = 2 };
        Assert.Equal(
            """{"schemaVersion":1,"id":"d1","title":"Weekly sync","meta":"Meeting minutes · Monday 5 October 2026, 4:00 PM","rows":[{"modules":[{"id":"m01","module":"executiveSummary","title":"Executive summary","textSize":"larger","linkToTranscript":true,"blocks":[]}]}],"record":null,"styleId":"corporate","version":2}""",
            Dot(JsonSerializer.Serialize(content, M4BridgeJsonContext.Default.DocumentContent)));

        var versions = new DocumentVersionsResult([new DocumentVersionInfo("20261007T101500123Z", new DateTimeOffset(2026, 10, 7, 11, 15, 0, TimeSpan.FromHours(1)), "generated", 3, 1)]);
        Assert.Equal(
            """{"versions":[{"id":"20261007T101500123Z","at":"2026-10-07T11:15:00+01:00","reason":"generated","changes":3,"version":1}]}""",
            JsonSerializer.Serialize(versions, M4BridgeJsonContext.Default.DocumentVersionsResult));

        var html = JsonSerializer.Serialize(new HtmlResult("<article class=\"paper\"></article>"), M4BridgeJsonContext.Default.HtmlResult);
        using var parsed = JsonDocument.Parse(html);
        Assert.Equal(["html"], parsed.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("<article class=\"paper\"></article>", parsed.RootElement.GetProperty("html").GetString());
    }

    [Fact]
    public void ATemplateAsTheUiSendsItReads()
    {
        const string ui = """
            {"template":{"id":"","name":"Our minutes","builtIn":false,"recordingTypes":["meeting"],
             "rows":[{"modules":[{"id":"m01","module":"decisions","instructions":"Bulleted.","length":"medium","textSize":"normal","linkToTranscript":true,"customTitle":null,"customText":null}]}],
             "inputs":{"transcript":true,"details":true,"participants":true,"agenda":true,"highlights":true,"attachments":false,"previousDocuments":false},
             "providerId":null,"styleId":"corporate","output":{"alsoExportDocx":true,"alsoExportMarkdown":false},"modifiedAt":"2026-10-07T10:00:00+01:00","documentKind":"minutes"}}
            """;

        var parameters = JsonSerializer.Deserialize(ui, M4BridgeJsonContext.Default.TemplateSaveParams)!;

        Assert.Equal("Our minutes", parameters.Template.Name);
        Assert.Equal("decisions", parameters.Template.Rows[0].Modules[0].Module);
        Assert.True(parameters.Template.Output.AlsoExportDocx);
        Assert.Equal("minutes", parameters.Template.DocumentKind);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(ui.Replace("\"documentKind\"", "\"surprise\"", StringComparison.Ordinal), M4BridgeJsonContext.Default.TemplateSaveParams));
    }

    [Fact]
    public void MissingRequiredFieldsAreRefused()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"recordingId":"r1"}""", M4BridgeJsonContext.Default.DocumentParams));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("""{"jobId":"g1"}""", M4BridgeJsonContext.Default.GenerationConfirmParams));
        Assert.NotNull(JsonSerializer.Deserialize("""{"recordingId":null,"template":{"id":"","name":"x","builtIn":false,"recordingTypes":[],"rows":[],"inputs":null,"providerId":null,"styleId":"corporate","output":null,"modifiedAt":null},"styleId":"minimal"}""", M4BridgeJsonContext.Default.GenerationPreviewHtmlParams));
    }

    [Fact]
    public void AnExportEstimateItemNamesItsDocumentOnlyForTheDocumentsRow()
    {
        Assert.Equal(
            """{"component":"transcript","name":"a - transcript.md","bytes":10}""",
            JsonSerializer.Serialize(new ExportEstimateItem("transcript", "a - transcript.md", 10), M3BridgeJsonContext.Default.ExportEstimateItem));
        Assert.Equal(
            """{"component":"documents","name":"a - Meeting minutes.docx","bytes":10,"documentId":"d1"}""",
            JsonSerializer.Serialize(new ExportEstimateItem("documents", "a - Meeting minutes.docx", 10) { DocumentId = "d1" }, M3BridgeJsonContext.Default.ExportEstimateItem));
    }

    [Fact]
    public void EveryM4MethodIsRegisteredOnceItsHostIsAdded()
    {
        var names = typeof(BridgeMethodNames).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)
            .Where(n => n.Split('.')[0] is "modules" or "templates" or "styles" or "providers" or "generation" or "documents")
            .ToList();

        Assert.Equal(32, names.Count);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }
}
