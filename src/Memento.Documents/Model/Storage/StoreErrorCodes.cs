namespace Memento.Documents.Model.Storage;

/// <summary>Codes of <see cref="DocumentStoreException"/>, shared by the template and style stores.</summary>
public static class StoreErrorCodes
{
    /// <summary>No item has that id.</summary>
    public const string NotFound = "storeNotFound";

    /// <summary>The id is empty or uses characters other than lower-case letters, digits and hyphens.</summary>
    public const string InvalidId = "storeInvalidId";

    /// <summary>A built-in item cannot be deleted; it can be reset.</summary>
    public const string BuiltInCannotBeDeleted = "storeBuiltInCannotBeDeleted";

    /// <summary>Only built-in items can be reset.</summary>
    public const string NotBuiltIn = "storeNotBuiltIn";

    /// <summary>The file on disk is not readable JSON for the item, or was written by a newer version.</summary>
    public const string Unreadable = "storeUnreadable";
}
