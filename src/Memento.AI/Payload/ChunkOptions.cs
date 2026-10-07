namespace Memento.AI.Payload;

/// <summary>How <see cref="TranscriptChunker"/> cuts.</summary>
/// <param name="BudgetTokens">The most transcript tokens per chunk (Local: about 1,500 at 8k context, 3,000 at 16k).</param>
/// <param name="Counter">The provider's token counter (exact for Local).</param>
/// <param name="MinFill">A cut at a weaker boundary is preferred only if the chunk is at least this full; otherwise the chunk is filled to the budget.</param>
/// <param name="ChapterCutFill">A new chapter always starts a new chunk once the current one is at least this full.</param>
public sealed record ChunkOptions(int BudgetTokens, ITokenCounter Counter, double MinFill = 0.6, double ChapterCutFill = 0.2);
