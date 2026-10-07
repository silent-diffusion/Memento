using System.Text.Json;

namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Documents for <c>settings.set</c> (M4): an id, or <c>null</c> for the built-in; omitted keeps it.</summary>
public sealed record DocumentsSettingsPatch
{
    public JsonElement DefaultTemplateId { get; init; }

    public JsonElement DefaultStyleId { get; init; }
}
