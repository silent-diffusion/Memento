namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Documents › History for <c>settings.set</c>. Each omitted or <c>null</c> field keeps its value.</summary>
public sealed record HistorySettingsPatch
{
    public bool? KeepVersions { get; init; }

    public int? KeepDays { get; init; }
}
