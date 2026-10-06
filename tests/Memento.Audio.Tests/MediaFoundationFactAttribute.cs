using Memento.Audio.Codecs;

namespace Memento.Audio.Tests;

/// <summary>A fact that needs the Windows Media Foundation codecs; skipped on images without them.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MediaFoundationFactAttribute : FactAttribute
{
    public MediaFoundationFactAttribute()
    {
        if (!MediaFoundationRuntime.IsAvailable)
        {
            Skip = "Media Foundation or its FLAC encoder is not available on this machine.";
        }
    }
}
