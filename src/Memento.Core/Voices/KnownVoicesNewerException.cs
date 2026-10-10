namespace Memento.Core.Voices;

/// <summary><c>voices/known.json</c> was written by a newer Memento (schema <see cref="Version"/>); it is left as it is.</summary>
public sealed class KnownVoicesNewerException : Exception
{
    public KnownVoicesNewerException(int version)
        : base($"voices/known.json has schema {version}; this Memento reads up to {KnownVoicesDocument.CurrentSchemaVersion}.")
    {
        Version = version;
    }

    public KnownVoicesNewerException()
        : this(0)
    {
    }

    public KnownVoicesNewerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int Version { get; }
}
