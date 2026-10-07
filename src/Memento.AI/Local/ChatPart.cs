namespace Memento.AI.Local;

/// <summary>
/// A piece of a rendered chat prompt. Template pieces carry the model's control tokens and are tokenized with
/// special-token parsing; content pieces (system prompt, turns) are tokenized as plain text, so a transcript that
/// contains <c>&lt;|im_end|&gt;</c> or <c>[INST]</c> cannot end a turn or inject one.
/// </summary>
public sealed record ChatPart(string Text, bool IsContent);
