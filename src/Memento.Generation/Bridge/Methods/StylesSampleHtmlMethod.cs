using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>styles.sampleHtml</c>: the Style editor's live sample page.</summary>
public sealed class StylesSampleHtmlMethod(StyleService styles) : M4Method<StyleSampleParams, HtmlResult>
{
    public override string Name => BridgeMethodNames.StylesSampleHtml;

    public override JsonTypeInfo<StyleSampleParams> ParamsTypeInfo => M4BridgeJsonContext.Default.StyleSampleParams;

    public override JsonTypeInfo<HtmlResult> ResultTypeInfo => M4BridgeJsonContext.Default.HtmlResult;

    public override Task<HtmlResult> InvokeAsync(StyleSampleParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(styles.SampleHtml(parameters.Settings ?? throw M4Errors.Invalid("styles.sampleHtml needs settings.", "settings")));
}
