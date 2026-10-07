namespace Memento.Documents.Tests.Support;

/// <summary>Character error rate: the edit distance between recognized and expected text over the expected length.</summary>
internal static class CharacterErrorRate
{
    public static double Of(string expected, string actual)
    {
        if (expected.Length == 0)
        {
            return actual.Length == 0 ? 0 : 1;
        }

        var previous = new int[actual.Length + 1];
        var current = new int[actual.Length + 1];
        for (var j = 0; j <= actual.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= expected.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= actual.Length; j++)
            {
                var substitution = previous[j - 1] + (expected[i - 1] == actual[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j] + 1, current[j - 1] + 1));
            }

            (previous, current) = (current, previous);
        }

        return previous[actual.Length] / (double)expected.Length;
    }
}
