namespace Memento.Documents.Tests.Support;

/// <summary>A fact that needs Windows text recognition; skipped when no OCR language is installed (CI images have none).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OcrFactAttribute : FactAttribute
{
    public OcrFactAttribute()
    {
        if (!OcrSupport.IsAvailable)
        {
            Skip = OcrSupport.SkipReason;
        }
    }
}
