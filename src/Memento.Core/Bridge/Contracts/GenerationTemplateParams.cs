namespace Memento.Core.Bridge.Contracts;

/// <summary><c>generation.preview</c>: the recording and the template as it is in the Builder now.</summary>
public sealed record GenerationTemplateParams(string RecordingId, Template Template);
