namespace Memento.Core.Serialization;

/// <summary>What <see cref="NullMembers.Repair"/> changed and what it could not.</summary>
/// <param name="Repaired">JSON paths of members that took their initial value; <c>items[]</c> means null entries were dropped from <c>items</c>.</param>
/// <param name="Unrepaired">JSON paths of null members or entries left as they are; the file should be refused.</param>
public sealed record NullRepair(IReadOnlyList<string> Repaired, IReadOnlyList<string> Unrepaired)
{
    /// <summary>The message a loader puts in its <see cref="System.Text.Json.JsonException"/> when something is left.</summary>
    public string Describe(string file) =>
        $"{file} has no value for {string.Join(", ", Unrepaired.Take(5).Select(p => $"'{p}'"))}{(Unrepaired.Count > 5 ? $" and {Unrepaired.Count - 5} more" : string.Empty)}, which cannot be null.";
}
