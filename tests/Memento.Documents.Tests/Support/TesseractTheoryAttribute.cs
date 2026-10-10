namespace Memento.Documents.Tests.Support;

/// <summary>A theory that runs the real Tesseract in Memento.Worker (<see cref="TesseractSupport"/>); skipped without it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TesseractTheoryAttribute : TheoryAttribute
{
    public TesseractTheoryAttribute()
    {
        Skip = TesseractSupport.SkipReason;
    }
}
