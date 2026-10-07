using System.Text.Json;
using Memento.Core.Agendas;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Core.Tests.M3;

/// <summary>
/// A <see cref="BridgeTestHost"/> for M3: a fake file picker, startup entry and agenda reader, the secrets file and
/// held agenda files in the test folder, and copy-only FLAC, MP3 and AAC encoders with a WAV verifier so storage,
/// export and reclaim run without Media Foundation.
/// </summary>
internal sealed class M3Host : IDisposable
{
    public M3Host(bool settingsLibrary = false, Action<IServiceCollection>? configure = null)
    {
        Directory = new TempDirectory();
        Host = new BridgeTestHost(
            directory: Directory,
            configure: services =>
            {
                services.Replace(ServiceDescriptor.Singleton<IFilePicker>(Picker));
                services.Replace(ServiceDescriptor.Singleton<IStartupRegistration>(Startup));
                services.Replace(ServiceDescriptor.Singleton<IAgendaReader>(Agenda));
                services.Replace(ServiceDescriptor.Singleton(new SecretStoreOptions(Directory.File("secrets.bin"))));
                services.Replace(ServiceDescriptor.Singleton(new PendingAgendaOptions(Directory.File("pending"), PendingAgendaOptions.DefaultExpiry)));
                services.AddSingleton<IAudioEncoder>(Flac);
                services.AddSingleton<IAudioEncoder>(Mp3);
                services.AddSingleton<IAudioEncoder>(new CopyEncoder("aac", ".m4a", lossless: false));
                services.AddSingleton<IAudioFileVerifier, WavVerifier>();
                if (settingsLibrary)
                {
                    services.Replace(ServiceDescriptor.Singleton<ILibraryLocation, SettingsLibraryLocation>());
                }

                configure?.Invoke(services);
            });
        if (settingsLibrary)
        {
            Host.Settings.UpdateAsync(s => s with { LibraryPath = Directory.File("Library") }, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    public TempDirectory Directory { get; }

    public BridgeTestHost Host { get; }

    public FakeFilePicker Picker { get; } = new();

    public FakeStartup Startup { get; } = new();

    public FakeAgendaReader Agenda { get; } = new();

    public CopyEncoder Flac { get; } = new("flac", ".flac", lossless: true);

    public CopyEncoder Mp3 { get; } = new("mp3", ".mp3", lossless: false);

    public RecordingEventSink Sink => Host.Sink;

    public T Get<T>()
        where T : notnull => Host.Get<T>();

    public Task<JsonElement> CallAsync(string method, object parameters) => Host.CallAsync(method, JsonSerializer.Serialize(parameters));

    public Task<JsonElement> ResultAsync(string method, object parameters) => Host.ResultAsync(method, JsonSerializer.Serialize(parameters));

    /// <summary>The error code of a call that must fail.</summary>
    public async Task<JsonElement> ErrorAsync(string method, object parameters)
    {
        var response = await CallAsync(method, parameters);
        Assert.True(response.TryGetProperty("error", out var error), $"{method} should have failed but answered {response}");
        return error;
    }

    /// <summary>A stored recording of <paramref name="seconds"/> of simulated microphone audio.</summary>
    public Task<string> RecordAsync(string title = "Weekly sync", double seconds = 3, params string[] sources) => Host.RecordAsync(title, seconds, sources);

    /// <summary>Writes <c>transcript.json</c> for the recording (three lines by two speakers by default).</summary>
    public string WriteTranscript(string recordingId, TranscriptDocument? transcript = null)
    {
        var path = Path.Combine(Host.Store.GetProjectFolder(recordingId), ProjectLayout.TranscriptFile);
        File.WriteAllText(path, JsonSerializer.Serialize(transcript ?? TranscriptFixtures.Meeting(), TranscriptJsonContext.Default.TranscriptDocument));
        return path;
    }

    /// <summary>A file in the test folder (outside the library).</summary>
    public string WriteFile(string name, string content)
    {
        var path = Directory.File(name);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        Host.Dispose();
        Directory.Dispose();
    }

    internal sealed class FakeFilePicker : IFilePicker
    {
        public string? Answer { get; set; }

        public List<(string Title, IReadOnlyList<FileFilter> Filters)> Calls { get; } = [];

        public Task<string?> PickFileAsync(string title, IReadOnlyList<FileFilter> filters, CancellationToken cancellationToken)
        {
            Calls.Add((title, filters));
            return Task.FromResult(Answer);
        }
    }

    internal sealed class FakeStartup : IStartupRegistration
    {
        public bool IsEnabled { get; private set; }

        public bool Refuse { get; set; }

        public void SetEnabled(bool enabled)
        {
            if (Refuse)
            {
                throw new UnauthorizedAccessException("refused");
            }

            IsEnabled = enabled;
        }
    }

    /// <summary>One item per non-empty line; a file whose text starts with "ERR" is unreadable.</summary>
    internal sealed class FakeAgendaReader : IAgendaReader
    {
        public Task<AgendaReading> ReadFileAsync(string path, CancellationToken cancellationToken)
        {
            var text = File.ReadAllText(path);
            if (text.StartsWith("ERR", StringComparison.Ordinal))
            {
                throw new BridgeException(DomainErrorCodes.AgendaUnreadable, $"{Path.GetFileName(path)} could not be read. Nothing was imported.");
            }

            var kind = Path.GetExtension(path) == ".docx" ? AgendaSourceKinds.Docx : AgendaSourceKinds.Text;
            return Task.FromResult(Read(text, kind));
        }

        public Task<AgendaReading> ReadTextAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(Read(text, AgendaSourceKinds.PastedText));

        private static AgendaReading Read(string text, string kind)
        {
            var items = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select((line, i) => new AgendaParsedItem(line.TrimEnd('?'), line.EndsWith('?'), line.EndsWith('?') ? "It may be two items." : null, 0, $"line {i + 1}"))
                .ToList();
            return new AgendaReading(kind, null, items, [new AgendaWarning("detailsSkipped", "Meeting details were left out.")], null);
        }
    }

    /// <summary>"Encodes" by copying the bytes (the content stays WAV, which the WAV decoder and verifier read).</summary>
    internal sealed class CopyEncoder(string codec, string extension, bool lossless) : IAudioEncoder
    {
        public string Codec => codec;

        public string FileExtension => extension;

        public bool IsLossless => lossless;

        public int Calls { get; private set; }

        /// <summary>When set, every encode waits for it (to cancel an export part-way).</summary>
        public TaskCompletionSource? Gate { get; set; }

        public async Task EncodeAsync(string sourceWavPath, string destinationPath, AudioEncodeOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken);
            }

            File.Copy(sourceWavPath, destinationPath, overwrite: true);
        }
    }

    internal sealed class WavVerifier : IAudioFileVerifier
    {
        public Task<AudioFileCheck> VerifyAsync(string path, CancellationToken cancellationToken)
        {
            var info = WavInfo.Read(path);
            return Task.FromResult(new AudioFileCheck(info.Format.SampleRate, info.Format.Channels, info.DurationMs));
        }
    }
}
