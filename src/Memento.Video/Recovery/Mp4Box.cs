namespace Memento.Video.Recovery;

/// <summary>An ISO BMFF box: its four-character type, where it starts, its full size and the size of its header.</summary>
public readonly record struct Mp4Box(string Type, long Offset, long Size, int HeaderSize)
{
    public long End => Offset + Size;

    public long PayloadOffset => Offset + HeaderSize;

    public long PayloadSize => Size - HeaderSize;
}
