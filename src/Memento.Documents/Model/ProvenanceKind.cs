using System.Text.Json.Serialization;

namespace Memento.Documents.Model;

/// <summary>Where a module's content came from.</summary>
[JsonConverter(typeof(CamelCaseEnumConverter<ProvenanceKind>))]
public enum ProvenanceKind
{
    /// <summary>Written by the AI provider; the claims are listed in <see cref="Provenance.ClaimIds"/>.</summary>
    Ai,

    /// <summary>Written by the user (Custom text, a hand-written document).</summary>
    User,

    /// <summary>Placed as data by the composer with no AI involved (Full transcript, Participants, Chapters).</summary>
    Data,
}
