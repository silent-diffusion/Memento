using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Settings;

/// <summary>Settings › Export (M3). Nothing is written outside the library unless the user exports.</summary>
public sealed record ExportSettings
{
    public const int MaxFolderLength = 240;

    public bool SaveCopiesOutside { get; init; }

    /// <summary>A full path, or <c>null</c> when none was chosen.</summary>
    public string? DefaultFolder { get; init; }

    public bool AskWhereEachTime { get; set; } = true;

    public bool CreateSubfolder { get; set; } = true;

    public ExportSelection Defaults { get; set; } = ExportSelection.Default;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public string? Validate()
    {
        if (DefaultFolder is { } folder && (folder.Length > MaxFolderLength || !Path.IsPathFullyQualified(folder)))
        {
            return $"The default export folder must be a full path such as D:\\Exports, at most {MaxFolderLength} characters.";
        }

        return Export.ExportRules.Validate(Defaults);
    }
}
