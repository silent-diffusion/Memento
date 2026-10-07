namespace Memento.AI;

/// <summary>Counts the tokens a text takes for one model (exactly, or as an estimate that errs high).</summary>
public interface ITokenCounter
{
    /// <summary>The counter uses the model's own tokenizer.</summary>
    bool IsExact { get; }

    int Count(string text);
}
