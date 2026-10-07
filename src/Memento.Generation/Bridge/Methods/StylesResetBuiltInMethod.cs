using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>styles.resetBuiltIn</c>.</summary>
public sealed class StylesResetBuiltInMethod(StyleService styles) : M4Method<StyleIdParams, Style>
{
    public override string Name => BridgeMethodNames.StylesResetBuiltIn;

    public override JsonTypeInfo<StyleIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.StyleIdParams;

    public override JsonTypeInfo<Style> ResultTypeInfo => M4BridgeJsonContext.Default.Style;

    public override Task<Style> InvokeAsync(StyleIdParams parameters, CancellationToken cancellationToken) =>
        styles.ResetBuiltInAsync(parameters.StyleId, cancellationToken);
}
