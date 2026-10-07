using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Updates;

/// <summary>The update download the status footer shows; <see cref="UpdateFooterStatus.Idle"/> otherwise.</summary>
public sealed class UpdateStatusBoard
{
    private UpdateFooterStatus _current = UpdateFooterStatus.Idle;

    public UpdateFooterStatus Current => Volatile.Read(ref _current);

    public void Set(UpdateFooterStatus status) => Volatile.Write(ref _current, status ?? UpdateFooterStatus.Idle);
}
