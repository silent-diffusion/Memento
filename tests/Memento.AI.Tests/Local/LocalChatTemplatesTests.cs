using Memento.AI.Local;

namespace Memento.AI.Tests.Local;

/// <summary>
/// The chat formats against the Jinja templates dumped from the GGUF files (fixtures/templates): the expected strings
/// are what each template renders for a system message, a user turn and add_generation_prompt (Qwen: enable_thinking
/// unset), checked by hand in the October spike (logs/template_check.txt, "MANUAL" block) and confirmed by the
/// models answering with end-of-generation.
/// </summary>
public sealed class LocalChatTemplatesTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "templates");

    [Fact]
    public void QwenMatchesTheVerifiedRender()
    {
        var text = LocalChatTemplates.Render(LocalChatTemplates.Qwen35, "SYSTEM TEXT", [AiMessage.User("USER TEXT")]);

        Assert.Equal("<|im_start|>system\nSYSTEM TEXT<|im_end|>\n<|im_start|>user\nUSER TEXT<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n", text);
    }

    [Fact]
    public void MinistralMatchesTheVerifiedRender()
    {
        var text = LocalChatTemplates.Render(LocalChatTemplates.Ministral3, "SYSTEM TEXT", [AiMessage.User("USER TEXT")]);

        // llama_chat_apply_template renders "[SYSTEM_PROMPT] SYSTEM TEXT…" with spaces and no BOS: wrong for this model.
        Assert.Equal("<s>[SYSTEM_PROMPT]SYSTEM TEXT[/SYSTEM_PROMPT][INST]USER TEXT[/INST]", text);
    }

    [Fact]
    public void EveryPieceQwenWritesComesFromItsJinjaTemplate()
    {
        var jinja = Template("qwen3.5");

        Assert.Contains("{{- '<|im_start|>system\\n' + content + '<|im_end|>\\n' }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '<|im_start|>' + message.role + '\\n' + content + '<|im_end|>' + '\\n' }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '<|im_start|>' + message.role + '\\n' + content }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '<|im_end|>\\n' }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '<|im_start|>assistant\\n' }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '<think>\\n\\n</think>\\n\\n' }}", jinja, StringComparison.Ordinal);
        Assert.Contains("render_content(message.content, true)|trim", jinja, StringComparison.Ordinal);
        Assert.Contains("render_content(messages[0].content, false, true)|trim", jinja, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPieceMinistralWritesComesFromItsJinjaTemplate()
    {
        var jinja = Template("ministral3");

        Assert.Contains("{{- bos_token }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '[SYSTEM_PROMPT]' -}}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '[/SYSTEM_PROMPT]' -}}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- '[INST]' + message['content'] + '[/INST]' }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- message['content'] }}", jinja, StringComparison.Ordinal);
        Assert.Contains("{{- eos_token }}", jinja, StringComparison.Ordinal);
        Assert.DoesNotContain("add_generation_prompt", jinja, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiTurnConversationsFollowEachTemplate()
    {
        IReadOnlyList<AiMessage> turns = [AiMessage.User("  First?  "), AiMessage.Assistant(" Answer. "), AiMessage.User("Second?")];

        var qwen = LocalChatTemplates.Render(LocalChatTemplates.Qwen35, " Rules. ", turns);
        var ministral = LocalChatTemplates.Render(LocalChatTemplates.Ministral3, "Rules.", turns);

        // Qwen trims every content (|trim); an assistant turn before the last user query has no think block.
        Assert.Equal(
            "<|im_start|>system\nRules.<|im_end|>\n<|im_start|>user\nFirst?<|im_end|>\n<|im_start|>assistant\nAnswer.<|im_end|>\n<|im_start|>user\nSecond?<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n",
            qwen);

        // Ministral does not trim; an assistant turn ends with the end-of-sequence token.
        Assert.Equal("<s>[SYSTEM_PROMPT]Rules.[/SYSTEM_PROMPT][INST]  First?  [/INST] Answer. </s>[INST]Second?[/INST]", ministral);
    }

    [Fact]
    public void WithoutASystemPromptNoSystemBlockIsWritten()
    {
        Assert.Equal("<|im_start|>user\nHi<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n", LocalChatTemplates.Render(LocalChatTemplates.Qwen35, string.Empty, [AiMessage.User("Hi")]));
        Assert.Equal("<s>[INST]Hi[/INST]", LocalChatTemplates.Render(LocalChatTemplates.Ministral3, null, [AiMessage.User("Hi")]));
    }

    [Fact]
    public void TurnsMustAlternateAndEndWithTheUser()
    {
        Assert.Throws<ArgumentException>(() => LocalChatTemplates.Render(LocalChatTemplates.Qwen35, "s", [AiMessage.User("a"), AiMessage.User("b")]));
        Assert.Throws<ArgumentException>(() => LocalChatTemplates.Render(LocalChatTemplates.Ministral3, "s", [AiMessage.User("a"), AiMessage.Assistant("b")]));
        Assert.Throws<ArgumentException>(() => LocalChatTemplates.Render(LocalChatTemplates.Qwen35, "s", []));
        Assert.Throws<ArgumentException>(() => LocalChatTemplates.Render("gemma4", "s", [AiMessage.User("a")]));
    }

    private static string Template(string id) => File.ReadAllText(Path.Combine(Fixtures, id + ".jinja"));
}
