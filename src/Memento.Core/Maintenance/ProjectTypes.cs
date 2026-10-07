namespace Memento.Core.Maintenance;

/// <summary>Recording types (BRIDGE.md <c>RecordingType</c>): the built-in ones or any custom name of 1–40 characters.</summary>
public static class ProjectTypes
{
    public const int MaxLength = 40;

    public static IReadOnlyList<string> BuiltIn { get; } = ["meeting", "interview", "presentation", "lecture", "dictation", "research", "general"];
}
