using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>styles.delete</c>: refused for presets (<c>styles.builtIn</c>) and for a template's default style (<c>styles.inUse</c>).</summary>
public sealed class StylesDeleteMethod(StyleService styles) : M4Method<StyleIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.StylesDelete;

    public override JsonTypeInfo<StyleIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.StyleIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(StyleIdParams parameters, CancellationToken cancellationToken) =>
        Done(styles.DeleteAsync(parameters.StyleId, cancellationToken));
}
