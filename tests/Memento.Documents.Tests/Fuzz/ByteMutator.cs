namespace Memento.Documents.Tests.Fuzz;

/// <summary>
/// Deterministic, format-blind mutations of a byte array: bit flips, truncation, chunk duplication and deletion, byte
/// insertion and "interesting" values (0, 0xFF, int boundaries) written over the content. One to three are stacked.
/// </summary>
internal static class ByteMutator
{
    private static readonly byte[][] InterestingValues =
    [
        [0x00],
        [0xFF],
        [0x7F],
        [0x80],
        [0xFF, 0xFF],
        [0x00, 0x00, 0x00, 0x80],
        [0xFF, 0xFF, 0xFF, 0x7F],
        [0xFF, 0xFF, 0xFF, 0xFF],
        [0x00, 0x00, 0x00, 0x00],
        "<<"u8.ToArray(),
        ">>"u8.ToArray(),
        "\r\n"u8.ToArray(),
        "\""u8.ToArray(),
    ];

    public static (byte[] Bytes, string Description) Mutate(byte[] input, Random random)
    {
        var bytes = input;
        var steps = new List<string>();
        var count = 1 + random.Next(3);
        for (var i = 0; i < count; i++)
        {
            (bytes, var step) = MutateOnce(bytes, random);
            steps.Add(step);
        }

        return (bytes, string.Join(", ", steps));
    }

    public static (byte[] Bytes, string Description) MutateOnce(byte[] input, Random random)
    {
        if (input.Length == 0)
        {
            return ([(byte)random.Next(256)], "one byte into an empty input");
        }

        switch (random.Next(6))
        {
            case 0:
            {
                var bytes = (byte[])input.Clone();
                var flips = 1 + random.Next(8);
                for (var f = 0; f < flips; f++)
                {
                    bytes[random.Next(bytes.Length)] ^= (byte)(1 << random.Next(8));
                }

                return (bytes, $"{flips} bit flips");
            }

            case 1:
            {
                var length = random.Next(input.Length);
                return (input[..length], $"truncated to {length}");
            }

            case 2:
            {
                var start = random.Next(input.Length);
                var length = 1 + random.Next(Math.Min(input.Length - start, 4096));
                var at = random.Next(input.Length + 1);
                var times = 1 + random.Next(random.Next(2) == 0 ? 4 : 64);
                var chunk = input.AsSpan(start, length).ToArray();
                var bytes = new List<byte>(input.Length + (length * times));
                bytes.AddRange(input.AsSpan(0, at).ToArray());
                for (var t = 0; t < times; t++)
                {
                    bytes.AddRange(chunk);
                }

                bytes.AddRange(input.AsSpan(at).ToArray());
                return (bytes.ToArray(), $"{length} bytes at {start} duplicated {times}x at {at}");
            }

            case 3:
            {
                var at = random.Next(input.Length + 1);
                var inserted = new byte[1 + random.Next(16)];
                random.NextBytes(inserted);
                return ([.. input.AsSpan(0, at), .. inserted, .. input.AsSpan(at)], $"{inserted.Length} random bytes inserted at {at}");
            }

            case 4:
            {
                var start = random.Next(input.Length);
                var length = 1 + random.Next(Math.Min(input.Length - start, 512));
                return ([.. input.AsSpan(0, start), .. input.AsSpan(start + length)], $"{length} bytes deleted at {start}");
            }

            default:
            {
                var bytes = (byte[])input.Clone();
                var value = InterestingValues[random.Next(InterestingValues.Length)];
                var at = random.Next(bytes.Length);
                var length = Math.Min(value.Length, bytes.Length - at);
                value.AsSpan(0, length).CopyTo(bytes.AsSpan(at));
                return (bytes, $"value {Convert.ToHexString(value)} written at {at}");
            }
        }
    }
}
