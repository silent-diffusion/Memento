namespace Memento.Documents.Tests.Support;

/// <summary>A fact that runs the real Tesseract in Memento.Worker (<see cref="TesseractSupport"/>); skipped without it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TesseractFactAttribute : FactAttribute
{
    public TesseractFactAttribute()
    {
        Skip = TesseractSupport.SkipReason;
    }
}
