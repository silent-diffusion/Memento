namespace Memento.Core.Projects;

/// <summary>Who made a chapter, highlight or topic.</summary>
public static class AnnotationOrigins
{
    public const string User = "user";
    public const string Local = "local";
    public const string Ai = "ai";

    public static bool IsValid(string? origin) => origin is User or Local or Ai;
}
