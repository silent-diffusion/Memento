using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>styles.list</c>: presets first.</summary>
public sealed class StylesListMethod(StyleService styles) : M4Method<EmptyParams, StylesListResult>
{
    public override string Name => BridgeMethodNames.StylesList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => M4BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<StylesListResult> ResultTypeInfo => M4BridgeJsonContext.Default.StylesListResult;

    public override Task<StylesListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Wrap(styles.ListAsync(cancellationToken), s => new StylesListResult(s));
}
