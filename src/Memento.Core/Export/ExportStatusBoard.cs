using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Export;

/// <summary>The export the status footer shows ("Exporting {title} · 42%"); <see cref="ExportFooterStatus.Idle"/> otherwise.</summary>
public sealed class ExportStatusBoard
{
    private ExportFooterStatus _current = ExportFooterStatus.Idle;

    public ExportFooterStatus Current => Volatile.Read(ref _current);

    public void Set(ExportFooterStatus status) => Volatile.Write(ref _current, status ?? ExportFooterStatus.Idle);
}
