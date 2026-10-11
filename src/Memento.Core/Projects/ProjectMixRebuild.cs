namespace Memento.Core.Projects;

/// <summary>
/// A pending "Rebuild the mix" (Review › More › Reprocess, after 2.0.0-beta.1), kept in <c>project.json</c> until the
/// <c>mix</c> stage has replaced the mix, so a rebuild cut short by closing Memento runs again at the next start.
/// </summary>
/// <param name="EvenLevels">Rebuild with evened levels (<c>true</c>) or as recorded (<c>false</c>).</param>
/// <param name="RequestedAt">When it was asked for.</param>
public sealed record ProjectMixRebuild(bool EvenLevels, DateTimeOffset RequestedAt);
