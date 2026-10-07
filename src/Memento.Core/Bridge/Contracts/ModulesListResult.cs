namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>modules.list</c>.</summary>
public sealed record ModulesListResult(IReadOnlyList<ModuleInfo> Modules);
