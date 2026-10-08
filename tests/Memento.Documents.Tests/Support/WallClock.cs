using System.Globalization;

namespace Memento.Documents.Tests.Support;

/// <summary>
/// Wall-clock limits of the parser hardening and fuzz tests (security audit 2026-10-07). The nominal limits are what a
/// fixed parser meets with a wide margin on an idle machine (slowest measured run 1 s); they are multiplied by
/// <c>MEMENTO_TEST_TIME_SCALE</c> (default 3), because CI and a busy desktop run every test assembly at once. The
/// regressions these tests guard against took tens of seconds to minutes, or crashed.
/// </summary>
internal static class WallClock
{
    private static readonly double Scale =
        double.TryParse(Environment.GetEnvironmentVariable("MEMENTO_TEST_TIME_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0
            ? scale
            : 3;

    public static TimeSpan Limit(double nominalSeconds) => TimeSpan.FromSeconds(nominalSeconds * Scale);
}
