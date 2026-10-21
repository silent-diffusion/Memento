using System.Reflection;
using LLama;
using LLama.Abstractions;
using LLama.Exceptions;
using LLama.Extensions;
using LLama.Native;

namespace Memento.AI.Local;

/// <summary>
/// Loads weights with llama.cpp's extra buffer types off (<c>use_extra_bufts = false</c>), which LLamaSharp 0.27's
/// <see cref="IModelParams"/> does not expose: the CPU build otherwise repacks every Q4_K tensor into a private
/// interleaved copy beside the memory-mapped file (11.5 GB more for Qwen3.5 35B-A3B, with the same speed). The handle is
/// wrapped in <see cref="LLamaWeights"/> through its non-public constructor; <see cref="IsAvailable"/> says whether this
/// build of LLamaSharp has it (a unit test pins it).
/// </summary>
internal static class LlamaWeightsLoader
{
    private static readonly ConstructorInfo? FromHandle = typeof(LLamaWeights)
        .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .FirstOrDefault(c => c.GetParameters() is [{ } only] && only.ParameterType == typeof(SafeLlamaModelHandle));

    public static bool IsAvailable => FromHandle is not null;

    /// <summary>Loads <paramref name="parameters"/>' model without repacking; the normal load when this build cannot.</summary>
    /// <exception cref="LoadWeightsFailedException">llama.cpp could not load the file.</exception>
    public static LLamaWeights LoadWithoutRepack(IModelParams parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (FromHandle is null)
        {
            return LLamaWeights.LoadFromFile(parameters);
        }

        using (parameters.ToLlamaModelParams(out var native))
        {
            native.use_extra_bufts = false;
            var handle = SafeLlamaModelHandle.LoadFromFile(parameters.ModelPath, native);
            try
            {
                return (LLamaWeights)FromHandle.Invoke([handle]);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }
}
