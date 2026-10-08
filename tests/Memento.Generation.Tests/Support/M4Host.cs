using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.Core.Bridge;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Projects;
using Memento.Core.Secrets;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using Memento.Documents.Export;
using Memento.Generation.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Generation.Tests.Support;

/// <summary>
/// A <see cref="BridgeTestHost"/> with M4: templates, styles and the key store in the test folder, a catalog whose local
/// models are four-byte files, a provider factory that hands out the synthetic meeting's model stand-in (and counts what
/// it constructs), and a PDF printer that records the print HTML.
/// </summary>
internal sealed class M4Host : IDisposable
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public M4Host(Action<IServiceCollection>? configure = null)
    {
        Directory = new TempDirectory();
        Host = new BridgeTestHost(
            directory: Directory,
            configure: services =>
            {
                services.Replace(ServiceDescriptor.Singleton(new SecretStoreOptions(Directory.File("secrets.bin"))));
                services.Replace(ServiceDescriptor.Singleton(Catalog));
                services.AddSingleton<IPdfPrinter>(Pdf);
                services.AddMementoM4(Directory.File("templates"), Directory.File("styles"));
                services.Replace(ServiceDescriptor.Singleton<IAiProviderFactory>(Providers));
                configure?.Invoke(services);
            });
    }

    /// <summary>Core's catalog with the local models shrunk to four bytes, so a test can "install" them.</summary>
    public static ModelCatalog Catalog { get; } = TinyLlmCatalog();

    public TempDirectory Directory { get; }

    public BridgeTestHost Host { get; }

    public FakeProviderFactory Providers { get; } = new();

    public FakePdfPrinter Pdf { get; } = new();

    public RecordingEventSink Sink => Host.Sink;

    public T Get<T>()
        where T : notnull => Host.Get<T>();

    public void InstallLocalModel(string id = LocalModelCatalog.Ministral3ThreeB)
    {
        var entry = Catalog.Find(id)!;
        var path = Host.Models.PathOf(entry);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[entry.SizeBytes]);
        if (Host.Models.VerifyAsync(id, CancellationToken.None).GetAwaiter().GetResult() != Memento.Core.Models.ModelCheck.Verified)
        {
            throw new InvalidOperationException($"The test model {id} did not verify.");
        }
    }

    /// <summary>A stored recording of the synthetic meeting: its details and agenda, and transcript.json written directly.</summary>
    public async Task<string> CreateMeetingAsync()
    {
        var store = Host.Store;
        var source = SyntheticMeeting.Manifest();
        var created = await store.CreateAsync(new ProjectCreateRequest(source.Details.Title, "meeting", source.CreatedAt, ProjectStates.Ready), CancellationToken.None);
        var manifest = await store.UpdateAsync(created.Id, m => m with { State = ProjectStates.Ready, DurationMs = source.DurationMs, Details = source.Details }, CancellationToken.None);
        await Host.Index.UpsertAsync(manifest, CancellationToken.None);
        await Host.Transcripts.UpdateAsync(created.Id, TranscriptChangeReasons.Transcribed, new Memento.Core.Settings.HistorySettings(), _ => SyntheticMeeting.Transcript(), CancellationToken.None);
        return created.Id;
    }

    public Task<JsonElement> CallAsync(string method, object parameters) => Host.CallAsync(method, JsonSerializer.Serialize(parameters, Web));

    public Task<JsonElement> ResultAsync(string method, object parameters) => Host.ResultAsync(method, JsonSerializer.Serialize(parameters, Web));

    /// <summary>The error of a call that must fail.</summary>
    public async Task<JsonElement> ErrorAsync(string method, object parameters)
    {
        var response = await CallAsync(method, parameters);
        Assert.True(response.TryGetProperty("error", out var error), $"{method} should have failed but answered {response}");
        return error;
    }

    /// <summary>The built-in Meeting minutes template as the Builder sends it.</summary>
    public async Task<JsonElement> MeetingMinutesAsync(string? providerId = null)
    {
        var template = await ResultAsync("templates.get", new { templateId = "meeting-minutes" });
        if (providerId is null)
        {
            return template;
        }

        var node = System.Text.Json.Nodes.JsonNode.Parse(template.GetRawText())!;
        node["providerId"] = providerId;
        return JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
    }

    /// <summary>Waits for the job's final <c>generation.progress</c> and returns its payload.</summary>
    public async Task<JsonElement> FinishedAsync(string jobId)
    {
        JsonElement? final = null;
        await TestRecordings.WaitUntilAsync(
            () =>
            {
                final = Events("generation.progress").LastOrDefault(e => e.GetProperty("jobId").GetString() == jobId && e.GetProperty("stage").GetString() is "done" or "failed" or "cancelled");
                return final is { ValueKind: JsonValueKind.Object };
            },
            "the generation to finish",
            20_000);
        return final!.Value;
    }

    public IReadOnlyList<JsonElement> Events(string name) =>
        Sink.Posted.Select(e => JsonDocument.Parse(e).RootElement.Clone())
            .Where(e => e.GetProperty("event").GetString() == name)
            .Select(e => e.GetProperty("payload"))
            .ToList();

    public void Dispose()
    {
        Host.Dispose();
        Directory.Dispose();
    }

    private static ModelCatalog TinyLlmCatalog()
    {
        var json = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            // The SHA-256 of the four zero bytes InstallLocalModel writes.
            models = ModelCatalog.Default.Entries.Select(e => e.Kind == ModelKinds.Llm ? e with { SizeBytes = 4, Sha256 = "df3f619804a92fdb4057192dc43dd748ea778adc52bc498ce80524c014b81119" } : e).ToList(),
        }, Web);
        return ModelCatalog.Parse(json);
    }

    /// <summary>Hands out <see cref="MeetingProvider"/>s and counts every provider it was asked to construct.</summary>
    internal sealed class FakeProviderFactory : IAiProviderFactory
    {
        public int CloudCreated { get; private set; }

        public int LocalCreated { get; private set; }

        public List<(string Id, string Model)> Cloud { get; } = [];

        public List<LocalAiOptions> Local { get; } = [];

        /// <summary>Replaces the stand-in (a failing or blocking provider).</summary>
        public Func<AiProviderKind, IAiProvider>? Make { get; set; }

        public IAiProvider CreateCloud(string id, string model)
        {
            CloudCreated++;
            Cloud.Add((id, model));
            return Make?.Invoke(AiProviderKind.Cloud) ?? new MeetingProvider(AiProviderKind.Cloud, 1_000_000);
        }

        public IAiProvider CreateLocal(LocalModelEntry model, string modelPath, LocalAiOptions options, Func<long?> freeVram)
        {
            LocalCreated++;
            Local.Add(options);
            return Make?.Invoke(AiProviderKind.Local) ?? new MeetingProvider();
        }
    }

    /// <summary>Returns a small fake PDF and keeps the HTML and page settings it was given.</summary>
    internal sealed class FakePdfPrinter : IPdfPrinter
    {
        public List<(string Html, PdfPrintOptions Options)> Printed { get; } = [];

        public Task<byte[]> PrintAsync(string html, PdfPrintOptions options, CancellationToken cancellationToken)
        {
            Printed.Add((html, options));
            return Task.FromResult("%PDF-1.7\n% fake\n"u8.ToArray());
        }
    }
}
