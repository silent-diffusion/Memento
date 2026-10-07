using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.move</c>: copy, verify, switch, delete; progress through <c>library.moveProgress</c>.</summary>
public sealed class LibraryMoveMethod(LibraryMoveService move) : BridgeMethod<LibraryMoveParams, JobIdResult>
{
    public override string Name => BridgeMethodNames.LibraryMove;

    public override JsonTypeInfo<LibraryMoveParams> ParamsTypeInfo => M3BridgeJsonContext.Default.LibraryMoveParams;

    public override JsonTypeInfo<JobIdResult> ResultTypeInfo => M3BridgeJsonContext.Default.JobIdResult;

    public override Task<JobIdResult> InvokeAsync(LibraryMoveParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(new JobIdResult(move.StartMove(parameters.NewPath)));
}
