namespace Memento.Documents.Model;

/// <summary>Codes of <see cref="DocumentFormatException"/>.</summary>
public static class DocumentFormatErrorCodes
{
    /// <summary>Not valid JSON, or not the expected shape.</summary>
    public const string Invalid = "documentInvalid";

    /// <summary>Written by a newer version of Memento (a higher <c>schemaVersion</c>).</summary>
    public const string NewerVersion = "documentNewerVersion";

    /// <summary>Valid JSON that breaks a structural rule (a row with no modules or more than three, duplicate module ids).</summary>
    public const string Structure = "documentStructure";
}
