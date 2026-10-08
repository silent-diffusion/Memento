namespace Memento.Documents.Tests.Fuzz;

/// <summary>
/// One parser under fuzzing: its seed fixtures, a fixed random seed (so CI sees the same inputs every run), how an input
/// is mutated, and how it is parsed. <see cref="Run"/> may only fail with the parser's documented error.
/// </summary>
internal sealed record FuzzTarget(
    string Name,
    int Seed,
    IReadOnlyList<string> Fixtures,
    Func<byte[], Random, (byte[] Bytes, string Description)> Mutate,
    Func<byte[], string, CancellationToken, Task> Run);
