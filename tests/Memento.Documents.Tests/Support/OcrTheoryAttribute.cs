namespace Memento.Documents.Tests.Support;

/// <summary>A theory that needs Windows text recognition; skipped when no OCR language is installed (CI images have none).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OcrTheoryAttribute : TheoryAttribute
{
    public OcrTheoryAttribute()
    {
        if (!OcrSupport.IsAvailable)
        {
            Skip = OcrSupport.SkipReason;
        }
    }
}
