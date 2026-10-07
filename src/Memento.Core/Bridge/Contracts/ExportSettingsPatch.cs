using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Export for <c>settings.set</c>. Each omitted field keeps its value; <see cref="Defaults"/> replaces whole.</summary>
public sealed record ExportSettingsPatch
{
    public bool? SaveCopiesOutside { get; init; }

    /// <summary>A full folder path, or JSON <c>null</c> to forget the folder; left out, it keeps its value.</summary>
    public JsonElement DefaultFolder { get; init; }

    public bool? AskWhereEachTime { get; init; }

    public bool? CreateSubfolder { get; init; }

    public ExportSelection? Defaults { get; init; }
}
