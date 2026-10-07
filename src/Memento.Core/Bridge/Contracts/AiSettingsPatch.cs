
namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › AI and privacy for <c>settings.set</c>. Omitted fields keep their value.</summary>
public sealed record AiSettingsPatch
{
    public bool? Enabled { get; init; }

    public bool? AskBeforeSend { get; init; }

    public bool? KeepRecord { get; init; }

    public AiSharePatch? Share { get; init; }

    // No providers: keys change only through ai.setKey and ai.clearKey (M3 clarification 5).
}
