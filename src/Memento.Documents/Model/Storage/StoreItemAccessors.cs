namespace Memento.Documents.Model.Storage;

/// <summary>How <see cref="JsonItemStore{T}"/> reads and rewrites the fields it manages on a stored item.</summary>
internal sealed record StoreItemAccessors<T>(
    Func<T, string> Id,
    Func<T, string> Name,
    Func<T, int> SchemaVersion,
    int CurrentSchemaVersion,
    Func<T, string, string, T> WithIdAndName,
    Func<T, bool, bool, T> Mark);
