using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Models;

/// <summary>One downloadable model in <c>catalog.json</c>.</summary>
public sealed record ModelCatalogEntry
{
    /// <summary>Stable id used in Settings and on the bridge, e.g. <c>large-v3-turbo</c>.</summary>
    public required string Id { get; init; }

    /// <summary>The engine that runs it: <c>whisper</c>, <c>sherpa-onnx</c> or <c>tesseract</c>; also its folder under <c>models\</c>.</summary>
    public required string Engine { get; init; }

    /// <summary>One of <see cref="ModelKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>One of <see cref="ModelRoles"/> for speaker models; otherwise <c>null</c>.</summary>
    public string? Role { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>File name under <c>models\&lt;engine&gt;\</c>.</summary>
    public required string FileName { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the file.</summary>
    public required string Sha256 { get; init; }

    /// <summary>https download address.</summary>
    public required string Url { get; init; }

    /// <summary>SPDX license of the model weights.</summary>
    public required string License { get; init; }

    /// <summary>Measured context: speed, accuracy, requirements.</summary>
    public string Notes { get; init; } = string.Empty;

    /// <summary><c>gpu</c>, <c>cpu</c> or <c>either</c>.</summary>
    public required string RunsOn { get; init; }

    /// <summary>Free video memory the model needs on a graphics card; <c>null</c> for CPU-only models.</summary>
    public long? MinVramBytes { get; init; }

    /// <summary><c>gpu</c> or <c>cpu</c>: recommended on PCs of that kind; <c>any</c>: always; <c>null</c>: never.</summary>
    public string? RecommendedFor { get; init; }

    /// <summary>"Most accurate", "Fast on CPU".</summary>
    public required string AccuracyNote { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
