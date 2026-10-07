namespace Memento.Core.Bridge.Contracts;

/// <summary>Settings › Documents (M4): the template and style a new document starts from; <c>null</c> uses the built-ins.</summary>
public sealed record DocumentsSettingsSnapshot(string? DefaultTemplateId, string? DefaultStyleId);
