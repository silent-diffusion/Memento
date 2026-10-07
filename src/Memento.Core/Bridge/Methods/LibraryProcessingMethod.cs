using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.processing</c>: the processing card. Its <c>stages</c> list every stage, finished <c>stored</c> and <c>optimize</c> included; <c>meta</c> is the Library row.</summary>
public sealed class LibraryProcessingMethod(ILibraryIndex index, LibraryAvailability? availability = null) : BridgeMethod<EmptyParams, LibraryProcessingResult>
{
    public override string Name => BridgeMethodNames.LibraryProcessing;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<LibraryProcessingResult> ResultTypeInfo => BridgeJsonContext.Default.LibraryProcessingResult;

    public override async Task<LibraryProcessingResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        // A library that is not connected processes nothing; library.list says why.
        if (availability?.Unavailable is not null)
        {
            return new LibraryProcessingResult(null, 0);
        }

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
