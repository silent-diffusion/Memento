namespace Memento.AI.Local;

/// <summary>Loads a model for a job: plans the video memory budget, loads, checks the context, warms up.</summary>
public interface ILocalLlmEngineFactory
{
    /// <exception cref="LocalLlmException">
    /// <see cref="AiErrorCodes.ModelNotInstalled"/>, <see cref="AiErrorCodes.NotEnoughVram"/> (does not fit, null
    /// context handle, or a spill to shared memory) or <see cref="AiErrorCodes.ProviderError"/>.
    /// </exception>
    Task<ILocalLlmEngine> LoadAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken);
}
