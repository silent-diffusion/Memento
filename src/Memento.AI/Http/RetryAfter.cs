using System.Globalization;
using System.Net.Http.Headers;

namespace Memento.AI.Http;

/// <summary>Reads how long a provider asked to wait: <c>retry-after-ms</c>, then <c>retry-after</c> (seconds or an HTTP date).</summary>
internal static class RetryAfter
{
    public static TimeSpan? Read(HttpResponseHeaders headers, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(headers);
        if (headers.TryGetValues("retry-after-ms", out var ms)
            && double.TryParse(ms.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds)
            && milliseconds >= 0)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        if (headers.RetryAfter is { } retryAfter)
        {
            if (retryAfter.Delta is { } delta)
            {
                return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
            }

            if (retryAfter.Date is { } date)
            {
                var wait = date - now;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }
        }

        // Some proxies send fractional seconds, which the typed header rejects.
        if (headers.TryGetValues("retry-after", out var raw)
            && double.TryParse(raw.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return null;
    }
}
