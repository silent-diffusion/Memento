using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.processing</c>: the processing card. Its <c>stages</c> list every stage, finished <c>stored</c> and <c>optimize</c> included; <c>meta</c> is the Library row.</summary>
public sealed class LibraryProcessingMethod(ILibraryIndex index) : BridgeMethod<EmptyParams, LibraryProcessingResult>
{
    public override string Name => BridgeMethodNames.LibraryProcessing;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<LibraryProcessingResult> ResultTypeInfo => BridgeJsonContext.Default.LibraryProcessingResult;

    public override async Task<LibraryProcessingResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        var processing = await index.ListProcessingAsync(cancellationToken);
        if (processing.Count == 0)
        {
            return new LibraryProcessingResult(null, 0);
        }

        var first = processing[0];
        return new LibraryProcessingResult(
            new ProcessingCurrent(first.Summary.Id, first.Summary.Title, first.Summary, first.Stages),
            processing.Count - 1);
    }
}
