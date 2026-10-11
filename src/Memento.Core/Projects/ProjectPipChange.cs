namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> <c>videoPip[]</c> (schema v4): from <paramref name="AtMs"/> on the recording timeline the camera
/// inset was on or off in <paramref name="Corner"/>, as <c>recording.setPip</c> set it. The export's "One file" burns the
/// inset in where it was; Review starts from the last entry.
/// </summary>
public sealed record ProjectPipChange(long AtMs, bool Enabled, string Corner);
