namespace Memento.Generation.Documents;

/// <summary>Why a document was written (<see cref="Memento.Documents.Model.DocumentChange.Reason"/>, version reasons, <c>documents.changed</c>).</summary>
public static class DocumentChangeReasons
{
    public const string Generated = "generated";
    public const string Regenerated = "regenerated";
    public const string Edited = "edited";
    public const string Restored = "restored";
    public const string Created = "created";
    public const string Renamed = "renamed";
    public const string Deleted = "deleted";
}
