using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>styles.save</c>: a new id when <c>id</c> is empty; saving a preset creates a copy.</summary>
public sealed class StylesSaveMethod(StyleService styles) : M4Method<StyleSaveParams, Style>
{
    public override string Name => BridgeMethodNames.StylesSave;

    public override JsonTypeInfo<StyleSaveParams> ParamsTypeInfo => M4BridgeJsonContext.Default.StyleSaveParams;

    public override JsonTypeInfo<Style> ResultTypeInfo => M4BridgeJsonContext.Default.Style;

    public override Task<Style> InvokeAsync(StyleSaveParams parameters, CancellationToken cancellationToken) =>
        styles.SaveAsync(parameters.Style ?? throw M4Errors.Invalid("styles.save needs a style.", "style"), cancellationToken);
}
