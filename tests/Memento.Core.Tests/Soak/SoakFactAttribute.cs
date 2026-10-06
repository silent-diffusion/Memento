namespace Memento.Core.Tests.Soak;

/// <summary>
/// A fact that runs only when <c>MEMENTO_SOAK=1</c>, so the default test run stays fast. Run soaks with
/// <c>MEMENTO_SOAK=1 dotnet test --filter Category=Soak --logger "console;verbosity=detailed"</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SoakFactAttribute : FactAttribute
{
    public SoakFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("MEMENTO_SOAK") != "1")
        {
            Skip = "Soak test: set MEMENTO_SOAK=1 to run it (see SoakFactAttribute).";
        }
    }
}
