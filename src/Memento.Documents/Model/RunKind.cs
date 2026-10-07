using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>The kinds of text run (ARCHITECTURE.md §8).</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<RunKind>))]
public enum RunKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>Emphasised text; <see cref="Run.Style"/> says bold, italic or both.</summary>
    Emphasis,

    /// <summary>A reference to a transcript moment: <see cref="Run.T"/> seconds, shown as <see cref="Run.Text"/> ("18:42").</summary>
    Timestamp,

    /// <summary>A quiet aside in secondary ink, such as "(reached with 8 minutes left)".</summary>
    Note,
}
