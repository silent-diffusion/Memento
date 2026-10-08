namespace Memento.Transcription.Tests;

/// <summary>
/// A fact that transcribes <c>MEMENTO_SYNC_WAV</c> (a file made by <c>tools/e2e/sync-fixture.ps1</c>, with its truth in
/// <c>&lt;wav&gt;.json</c>) with the real worker; skipped unless the worker is built and the Whisper model named by
/// <c>MEMENTO_SYNC_MODEL</c> (default <c>ggml-large-v3-turbo.bin</c>, the default on a graphics card) is installed. Run
/// with <c>dotnet test --filter Category=Hardware</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SyncHardwareFactAttribute : FactAttribute
{
    public SyncHardwareFactAttribute()
    {
        if (WorkerBuild.Executable is null)
        {
            Skip = "Memento.Worker has not been built.";
        }
        else if (SyncFixture.Wav is null)
        {
            Skip = "Set MEMENTO_SYNC_WAV to a file made by tools/e2e/sync-fixture.ps1 to run the sync check.";
        }
        else if (!File.Exists(SyncFixture.ModelPath))
        {
            Skip = $"Model {SyncFixture.ModelPath} is not installed.";
        }
    }
}
