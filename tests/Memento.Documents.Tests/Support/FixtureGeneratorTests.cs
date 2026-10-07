namespace Memento.Documents.Tests.Support;

/// <summary>Rebuilds the binary fixtures in the source tree. Skipped unless <c>MEMENTO_REGENERATE_FIXTURES=1</c>.</summary>
public sealed class FixtureGeneratorTests
{
    [RegenerateFact]
    public void RegeneratesTheBinaryFixtures()
    {
        FixtureGenerator.WriteAll(FixturePaths.SourceDirectory);
        FixtureGenerator.WriteAll(FixturePaths.OutputDirectory);
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class RegenerateFactAttribute : FactAttribute
    {
        public RegenerateFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("MEMENTO_REGENERATE_FIXTURES") != "1")
            {
                Skip = "Set MEMENTO_REGENERATE_FIXTURES=1 to rebuild the binary fixtures.";
            }
        }
    }
}
