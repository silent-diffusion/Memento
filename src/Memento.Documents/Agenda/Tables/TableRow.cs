namespace Memento.Documents.Agenda.Tables;

/// <summary>One row of a table from any format (CSV, a Markdown table, a Word table, a sheet).</summary>
/// <param name="Cells">The cell texts, left to right; a cell may hold several lines.</param>
/// <param name="Location">Where the row is.</param>
internal sealed record TableRow(IReadOnlyList<string> Cells, AgendaSourceLocation Location)
{
    /// <summary>One merged cell spans the whole row (a section row in a sheet or a Word table).</summary>
    public bool MergedAcross { get; init; }

    /// <summary>The location of one cell (a spreadsheet reference such as B4); the row's location when not set.</summary>
    public Func<int, AgendaSourceLocation>? CellLocation { get; init; }

    public AgendaSourceLocation LocationOf(int column) => CellLocation?.Invoke(column) ?? Location;
}
