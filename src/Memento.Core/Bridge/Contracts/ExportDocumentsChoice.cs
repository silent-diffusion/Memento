namespace Memento.Core.Bridge.Contracts;

/// <summary>The documents row of the Export dialog. Documents arrive in M4; M3 writes nothing for this row.</summary>
public sealed record ExportDocumentsChoice
{
    public bool On { get; init; }

    public IReadOnlyList<string> DocumentIds { get; init; } = [];

    /// <summary><c>docx</c>, <c>pdf</c> or <c>markdown</c>.</summary>
    public string Format { get; init; } = "docx";
}
