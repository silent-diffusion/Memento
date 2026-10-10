namespace Memento.Core.Workers;

/// <summary>
/// The live transcript while recording (2.0, <see cref="WorkerJobKinds.Live"/>): the worker loads the model once and
/// stays loaded, transcribing each <c>audio</c> line (<see cref="LiveAudio"/>, a 10-second window of the mix) and
/// answering it with a <c>heard</c> line, until <c>end</c> or <c>cancel</c>. Short windows heard whole: no speech
/// packing or alignment (the full pass after Stop does that and replaces this draft).
/// </summary>
/// <param name="ModelId">Small, or Base when Small is not installed; never a model the full pass prefers on the card.</param>
/// <param name="Runtimes"><c>["cpu"]</c> by default; <c>["vulkan","cpu"]</c> only when Settings allows the card and it is free.</param>
/// <param name="Threads">Processor threads; kept low so recording and the rest of the PC keep theirs.</param>
public sealed record LiveJob(
    string ModelPath,
    string ModelId,
    IReadOnlyList<string> Runtimes,
    int GpuDevice,
    string? GpuName,
    string Language,
    string Prompt,
    int Threads);
