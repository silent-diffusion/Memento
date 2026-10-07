using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Speakers in <see cref="SettingsSnapshot"/>.</summary>
/// <param name="ExpectedSpeakers">The string <c>auto</c> or a number 1–20.</param>
/// <param name="RememberRenamed">Stored; not applied in this version.</param>
public sealed record SpeakersSettingsSnapshot(bool Identify, JsonElement ExpectedSpeakers, bool RememberRenamed, string EmbeddingModelId);
