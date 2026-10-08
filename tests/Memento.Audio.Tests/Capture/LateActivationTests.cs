using Memento.Audio.Capture;

namespace Memento.Audio.Tests.Capture;

/// <summary>A per-app activation that completes after Memento stopped waiting is released, not leaked.</summary>
public sealed class LateActivationTests
{
    [Fact]
    public async Task AClientThatArrivesAfterTheTimeoutIsReleased()
    {
        var activation = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new object();

        WasapiAudioCapture.ReleaseWhenCompleted(activation.Task, c => released.TrySetResult(c));
        Assert.False(released.Task.IsCompleted);
        activation.SetResult(client);

        Assert.Same(client, await released.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task AnActivationThatAlreadyFinishedIsReleasedAndAFailedOneReleasesNothing()
    {
        var calls = 0;
        WasapiAudioCapture.ReleaseWhenCompleted(Task.FromResult(new object()), _ => Interlocked.Increment(ref calls));
        WasapiAudioCapture.ReleaseWhenCompleted(Task.FromException<object>(new InvalidOperationException("activation failed")), _ => Interlocked.Increment(ref calls));
        var failing = new TaskCompletionSource<object>();
        WasapiAudioCapture.ReleaseWhenCompleted(failing.Task, _ => Interlocked.Increment(ref calls));
        failing.SetException(new InvalidOperationException("activation failed"));

        await Task.Delay(50);
        Assert.Equal(1, Volatile.Read(ref calls));
    }
}
