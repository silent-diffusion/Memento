using Memento.Audio.Sources;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Audio.Adapters;

/// <summary>
/// <see cref="IAudioSourceProvider"/> over <see cref="AudioSourceEnumerator"/>: microphones, system audio per output
/// device and running applications with an audio session, shaped exactly like BRIDGE.md's <c>AudioSource</c>
/// (<c>mic:&lt;endpointId&gt;</c>, <c>system:&lt;endpointId&gt;</c>, <c>app:&lt;pid&gt;</c>). Re-enumerates on every call,
/// off the caller's thread (COM enumeration takes a few milliseconds per device).
/// </summary>
public sealed class WasapiAudioSourceProvider(AudioSourceEnumerator enumerator) : IAudioSourceProvider
{
    public async Task<IReadOnlyList<AudioSource>> ListAsync(CancellationToken cancellationToken)
    {
        var sources = await Task.Run(() => enumerator.List(AudioSourceListOptions.Default), cancellationToken).ConfigureAwait(false);
        return sources.Select(ToContract).ToList();
    }

    public static AudioSource ToContract(AudioSourceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new AudioSource(info.Id, KindName(info.Kind), info.Name, info.Detail, info.IsDefault, info.Kind == AudioSourceKind.Application ? info.ProcessId : null);
    }

    public static string KindName(AudioSourceKind kind) => kind switch
    {
        AudioSourceKind.Microphone => "microphone",
        AudioSourceKind.System => "system",
        _ => "application",
    };
}
