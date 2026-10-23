namespace Memento.AI.Server;

/// <summary>
/// The server answered 400 (or 422) and its error names <c>response_format</c>, <c>json_schema</c> or a schema: it does
/// not take a strict JSON schema. <see cref="ServerProvider"/> sends the request once more with <c>json_object</c>.
/// Never leaves the provider.
/// </summary>
internal sealed class SchemaRefusedException : Exception
{
    public SchemaRefusedException()
        : base("The server does not accept a JSON schema.")
    {
    }

    public SchemaRefusedException(string message)
        : base(message)
    {
    }

    public SchemaRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
