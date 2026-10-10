using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Speakers in <see cref="SettingsSnapshot"/>.</summary>
/// <param name="ExpectedSpeakers">The string <c>auto</c> or a number 1–20.</param>
/// <param name="RememberRenamed">The 1.x switch, stored but never applied; superseded by <paramref name="RememberVoices"/>.</param>
/// <param name="RememberVoices">2.0: "Remember speakers by voice" (off by default).</param>
public sealed record SpeakersSettingsSnapshot(bool Identify, JsonElement ExpectedSpeakers, bool RememberRenamed, string EmbeddingModelId, bool RememberVoices);
