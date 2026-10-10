using Vortice.Direct3D11;

namespace Memento.Video.Sources;

/// <summary>
/// The latest picture a source holds. <see cref="Texture"/> belongs to the source and is only valid until the next
/// call into it; it is <c>null</c> for test sources. <see cref="QpcTicks"/> is when the picture was captured, on the
/// audio timeline's clock (<see cref="Memento.Audio.QpcClock"/>).
/// </summary>
public readonly record struct VideoFrame(ID3D11Texture2D? Texture, int Subresource, int Width, int Height, long QpcTicks, long Sequence);
