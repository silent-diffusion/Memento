namespace Memento.AI.Local;

/// <summary>
/// Chat formatting per model family from the catalog's verified template id (ENGINE-NOTES.md section H, trap 6:
/// llama.cpp's <c>LLamaTemplate</c> is heuristic, misrenders Ministral 3 and omits Qwen3.5's empty think block).
/// Each format reproduces the model's own Jinja chat template (the GGUF <c>tokenizer.chat_template</c>) for a system
/// prompt and alternating user/assistant turns, with the generation prompt appended. <see cref="RenderParts"/>
/// keeps control tokens apart from content so the engine can tokenize content without special-token parsing.
/// </summary>
public static class LocalChatTemplates
{
    /// <summary>Qwen3.5: ChatML with the empty <c>&lt;think&gt;</c> block that turns thinking off.</summary>
    public const string Qwen35 = "qwen3.5";

    /// <summary>Ministral 3 (Mistral v7 tekken): <c>&lt;s&gt;[SYSTEM_PROMPT]…[/SYSTEM_PROMPT][INST]…[/INST]</c>.</summary>
    public const string Ministral3 = "ministral3";

    public static IReadOnlyList<string> All { get; } = [Qwen35, Ministral3];

    public static bool IsKnown(string? templateId) => templateId is Qwen35 or Ministral3;

    /// <summary>The prompt the model reads, as one string; the answer is generated right after it.</summary>
    public static string Render(string templateId, string? system, IReadOnlyList<AiMessage> messages) =>
        string.Concat(RenderParts(templateId, system, messages).Select(p => p.Text));

    /// <summary>The prompt as template and content pieces, in order.</summary>
    /// <exception cref="ArgumentException">Unknown template, or turns that do not alternate starting and ending with a user turn.</exception>
    public static IReadOnlyList<ChatPart> RenderParts(string templateId, string? system, IReadOnlyList<AiMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0 || messages[0].Role != AiRole.User || messages[^1].Role != AiRole.User)
        {
            throw new ArgumentException("The conversation must start and end with a user turn.", nameof(messages));
        }

        for (var i = 1; i < messages.Count; i++)
        {
            if (messages[i].Role == messages[i - 1].Role)
            {
                throw new ArgumentException("User and assistant turns must alternate.", nameof(messages));
            }
        }

        return templateId switch
        {
            Qwen35 => Qwen35Parts(system, messages),
            Ministral3 => Ministral3Parts(system, messages),
            _ => throw new ArgumentException($"No verified chat template '{templateId}'.", nameof(templateId)),
        };
    }

    // Jinja (Qwen3.5-4B): system → '<|im_start|>system\n' + content|trim + '<|im_end|>\n'; user →
    // '<|im_start|>user\n' + content|trim + '<|im_end|>\n'; an assistant turn before the last user query →
    // '<|im_start|>assistant\n' + content|trim + '<|im_end|>\n'; generation prompt with enable_thinking unset →
    // '<|im_start|>assistant\n<think>\n\n</think>\n\n'.
    private static List<ChatPart> Qwen35Parts(string? system, IReadOnlyList<AiMessage> messages)
    {
        var parts = new List<ChatPart>();
        if (!string.IsNullOrEmpty(system))
        {
            parts.Add(new ChatPart("<|im_start|>system\n", false));
            parts.Add(new ChatPart(system.Trim(), true));
            parts.Add(new ChatPart("<|im_end|>\n", false));
        }

        foreach (var message in messages)
        {
            parts.Add(new ChatPart(message.Role == AiRole.User ? "<|im_start|>user\n" : "<|im_start|>assistant\n", false));
            parts.Add(new ChatPart(message.Content.Trim(), true));
            parts.Add(new ChatPart("<|im_end|>\n", false));
        }

        parts.Add(new ChatPart("<|im_start|>assistant\n<think>\n\n</think>\n\n", false));
        return Merge(parts);
    }

    // Jinja (Ministral-3-3B-Instruct-2512): bos_token, then '[SYSTEM_PROMPT]' + content + '[/SYSTEM_PROMPT]' when
    // the first message is a system message; user → '[INST]' + content + '[/INST]'; assistant → content + eos_token.
    // No generation suffix. Without a system message the template inserts its default "Le Chat" persona prompt;
    // Memento always passes its own instructions, and with none it sends no system block rather than that persona.
    private static List<ChatPart> Ministral3Parts(string? system, IReadOnlyList<AiMessage> messages)
    {
        var parts = new List<ChatPart> { new("<s>", false) };
        if (!string.IsNullOrEmpty(system))
        {
            parts.Add(new ChatPart("[SYSTEM_PROMPT]", false));
            parts.Add(new ChatPart(system, true));
            parts.Add(new ChatPart("[/SYSTEM_PROMPT]", false));
        }

        foreach (var message in messages)
        {
            if (message.Role == AiRole.User)
            {
                parts.Add(new ChatPart("[INST]", false));
                parts.Add(new ChatPart(message.Content, true));
                parts.Add(new ChatPart("[/INST]", false));
            }
            else
            {
                parts.Add(new ChatPart(message.Content, true));
                parts.Add(new ChatPart("</s>", false));
            }
        }

        return Merge(parts);
    }

    /// <summary>Joins neighbouring template pieces and drops empty ones.</summary>
    private static List<ChatPart> Merge(List<ChatPart> parts)
    {
        var merged = new List<ChatPart>();
        foreach (var part in parts.Where(p => p.Text.Length > 0))
        {
            if (merged.Count > 0 && !part.IsContent && !merged[^1].IsContent)
            {
                merged[^1] = new ChatPart(merged[^1].Text + part.Text, false);
            }
            else
            {
                merged.Add(part);
            }
        }

        return merged;
    }
}
