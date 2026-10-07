using System.Text;

namespace Memento.AI.Local;

/// <summary>
/// Chat formatting per model family from the catalog's verified template id (ENGINE-NOTES.md section H, trap 6:
/// llama.cpp's <c>LLamaTemplate</c> is heuristic, misrenders Ministral 3 and omits Qwen3.5's empty think block).
/// Each format reproduces the model's own Jinja chat template (the GGUF <c>tokenizer.chat_template</c>) for a system
/// prompt and alternating user/assistant turns, with the generation prompt appended. Special tokens are written as
/// text and tokenized with <c>special: true</c>.
/// </summary>
public static class LocalChatTemplates
{
    /// <summary>Qwen3.5: ChatML with the empty <c>&lt;think&gt;</c> block that turns thinking off.</summary>
    public const string Qwen35 = "qwen3.5";

    /// <summary>Ministral 3 (Mistral v7 tekken): <c>&lt;s&gt;[SYSTEM_PROMPT]…[/SYSTEM_PROMPT][INST]…[/INST]</c>.</summary>
    public const string Ministral3 = "ministral3";

    public static IReadOnlyList<string> All { get; } = [Qwen35, Ministral3];

    public static bool IsKnown(string? templateId) => templateId is Qwen35 or Ministral3;

    /// <summary>Renders the prompt the model reads; the answer is generated right after it.</summary>
    /// <exception cref="ArgumentException">Unknown template, or turns that do not alternate starting and ending with a user turn.</exception>
    public static string Render(string templateId, string? system, IReadOnlyList<AiMessage> messages)
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
            Qwen35 => RenderQwen35(system, messages),
            Ministral3 => RenderMinistral3(system, messages),
            _ => throw new ArgumentException($"No verified chat template '{templateId}'.", nameof(templateId)),
        };
    }

    // Jinja (Qwen3.5-4B): system → '<|im_start|>system\n' + content|trim + '<|im_end|>\n'; user →
    // '<|im_start|>user\n' + content|trim + '<|im_end|>\n'; an assistant turn before the last user query →
    // '<|im_start|>assistant\n' + content|trim + '<|im_end|>\n'; generation prompt with enable_thinking unset →
    // '<|im_start|>assistant\n<think>\n\n</think>\n\n'.
    private static string RenderQwen35(string? system, IReadOnlyList<AiMessage> messages)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(system))
        {
            builder.Append("<|im_start|>system\n").Append(system.Trim()).Append("<|im_end|>\n");
        }

        foreach (var message in messages)
        {
            builder.Append("<|im_start|>").Append(message.Role == AiRole.User ? "user" : "assistant").Append('\n')
                .Append(message.Content.Trim()).Append("<|im_end|>\n");
        }

        builder.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");
        return builder.ToString();
    }

    // Jinja (Ministral-3-3B-Instruct-2512): bos_token, then '[SYSTEM_PROMPT]' + content + '[/SYSTEM_PROMPT]' when
    // the first message is a system message; user → '[INST]' + content + '[/INST]'; assistant → content + eos_token.
    // No generation suffix. Without a system message the template inserts its default "Le Chat" persona prompt;
    // Memento always passes its own instructions, and with none it sends no system block rather than that persona.
    private static string RenderMinistral3(string? system, IReadOnlyList<AiMessage> messages)
    {
        var builder = new StringBuilder("<s>");
        if (!string.IsNullOrEmpty(system))
        {
            builder.Append("[SYSTEM_PROMPT]").Append(system).Append("[/SYSTEM_PROMPT]");
        }

        foreach (var message in messages)
        {
            if (message.Role == AiRole.User)
            {
                builder.Append("[INST]").Append(message.Content).Append("[/INST]");
            }
            else
            {
                builder.Append(message.Content).Append("</s>");
            }
        }

        return builder.ToString();
    }
}
