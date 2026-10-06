namespace Memento.Core.Projects;

/// <summary>A project file has a schema this build cannot write (made by a newer Memento) or cannot read.</summary>
public sealed class ProjectSchemaException : Exception
{
    public ProjectSchemaException(string message)
        : base(message)
    {
    }

    public ProjectSchemaException()
        : this("The project file has an unsupported schema.")
    {
    }

    public ProjectSchemaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
