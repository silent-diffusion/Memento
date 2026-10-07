using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Import;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.importMedia</c>: returns once the project exists; storing continues in the background.</summary>
public sealed class LibraryImportMediaMethod(MediaImportService imports) : BridgeMethod<LibraryImportMediaParams, LibraryImportMediaResult>
{
    public override string Name => BridgeMethodNames.LibraryImportMedia;

    public override JsonTypeInfo<LibraryImportMediaParams> ParamsTypeInfo => M3BridgeJsonContext.Default.LibraryImportMediaParams;

    public override JsonTypeInfo<LibraryImportMediaResult> ResultTypeInfo => M3BridgeJsonContext.Default.LibraryImportMediaResult;

    public override Task<LibraryImportMediaResult> InvokeAsync(LibraryImportMediaParams parameters, CancellationToken cancellationToken) =>
        imports.ImportAsync(parameters, cancellationToken);
}
