namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>video.thumbnail</c>.</summary>
public sealed record VideoThumbnailParams
{
    /// <summary>A <see cref="VideoSource.Id"/>.</summary>
    public required string SourceId { get; init; }

    /// <summary>Width of the still in pixels (16–640); the height follows the source's shape. 320 when left out.</summary>
    public int? Width { get; init; }
}
