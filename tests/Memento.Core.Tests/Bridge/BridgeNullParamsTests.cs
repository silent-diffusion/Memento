using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Security audit 2, §9, at the bridge: a list, object or list entry the page sends as <c>null</c> is refused by name
/// before any method runs; one it leaves out keeps its default.
/// </summary>
public sealed class BridgeNullParamsTests
{
    private const string Agenda = """ "recordingId": "20261022-090000-abcdef", "source": "Pasted text", "sourceKind": "text" """;

    [Fact]
    public async Task AListLeftOutReadsAsEmpty()
    {
        var method = new AgendaProbe();

        await method.HandleAsync(Parse("{" + Agenda + "}"), CancellationToken.None);

        Assert.NotNull(method.Received);
        Assert.Empty(method.Received.Items);
    }

    [Theory]
    [InlineData(""" "items": null """, "'items' is null")]
    [InlineData(""" "items": [null] """, "'items[0]' is null")]
    public async Task ANullListOrEntryIsRefusedByNameAndTheMethodNeverRuns(string member, string expected)
    {
        var method = new AgendaProbe();

        var error = await Assert.ThrowsAsync<BridgeException>(() => method.HandleAsync(Parse("{" + Agenda + "," + member + "}"), CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.Code);
        Assert.Contains("agenda.probe", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was changed", error.Message, StringComparison.Ordinal);
        Assert.Null(method.Received);
    }

    [Fact]
    public async Task ATemplateWithoutInputsOrOutputGetsTheDocumentedDefaults()
    {
        var method = new TemplateProbe();

        await method.HandleAsync(Parse("""{ "template": { "id": "", "name": "Minutes", "builtIn": false, "styleId": "corporate" } }"""), CancellationToken.None);

        var template = method.Received!.Template;
        Assert.Equal(InputSelection.Default, template.Inputs);
        Assert.Equal(TemplateOutputSettings.None, template.Output);
        Assert.Empty(template.RecordingTypes);
        Assert.Empty(template.Rows);
        Assert.Equal(string.Empty, template.DocumentKind);
    }

    [Fact]
    public async Task ANullDeepInsideTheParametersIsNamedByItsPath()
    {
        var method = new TemplateProbe();

        var error = await Assert.ThrowsAsync<BridgeException>(() => method.HandleAsync(
            Parse("""{ "template": { "id": "", "name": "Minutes", "builtIn": false, "styleId": "corporate", "rows": [ { "modules": [null] } ] } }"""),
            CancellationToken.None));

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.Code);
        Assert.Contains("'template.rows[0].modules[0]' is null", error.Message, StringComparison.Ordinal);
        Assert.Null(method.Received);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class AgendaProbe : BridgeMethod<AgendaApplyParams, EmptyResult>
    {
        public override string Name => "agenda.probe";

        public override JsonTypeInfo<AgendaApplyParams> ParamsTypeInfo => M3BridgeJsonContext.Default.AgendaApplyParams;

        public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M3BridgeJsonContext.Default.EmptyResult;

        public AgendaApplyParams? Received { get; private set; }

        public override Task<EmptyResult> InvokeAsync(AgendaApplyParams parameters, CancellationToken cancellationToken)
        {
            Received = parameters;
            return Task.FromResult(new EmptyResult());
        }
    }

    private sealed class TemplateProbe : BridgeMethod<TemplateSaveParams, EmptyResult>
    {
        public override string Name => "templates.probe";

        public override JsonTypeInfo<TemplateSaveParams> ParamsTypeInfo => M4BridgeJsonContext.Default.TemplateSaveParams;

        public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

        public TemplateSaveParams? Received { get; private set; }

        public override Task<EmptyResult> InvokeAsync(TemplateSaveParams parameters, CancellationToken cancellationToken)
        {
            Received = parameters;
            return Task.FromResult(new EmptyResult());
        }
    }
}
