using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Memento.Core.Video;
using Memento.Documents.Tests.Fuzz;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Memento.Core.Tests.Projects;

/// <summary>
/// Seeded, deterministic fuzzing of a project's video entries as a copied-in folder can hold them (security audit 2):
/// <c>project.json</c> at schema 4 and at schema 3 (through the v3 → v4 migration), mutated byte by byte with the agenda
/// harness's <see cref="ByteMutator"/> and field by field in <c>videoTracks[]</c> with hostile values (paths out of the
/// folder, UNC and device names, streams, wrong types, huge and negative numbers, duplicate keys, deep nesting).
/// <para>
/// Each load must end in a manifest or <see cref="ProjectNotFoundException"/>; a manifest that loads names no file
/// outside its folder, and the files Remove video would delete are MP4s in its <c>tracks/</c> folder. Before the audit's
/// fixes, a duplicated key or a version that is not a number escaped as ArgumentException or ProjectSchemaException.
/// </para>
/// </summary>
public sealed class ManifestVideoFuzzTests(ITestOutputHelper output) : IDisposable
{
    private const int Iterations = 1_500;

    private static readonly JsonNode?[] HostileValues =
    [
        null,
        "../../outside.mp4",
        "..\\..\\outside.mp4",
        "C:\\Windows\\win.ini",
        "\\\\attacker.example\\share\\x.mp4",
        "//attacker.example/share/x.mp4",
        "tracks/screen.mp4:secret",
        "tracks/CON.mp4",
        "tracks/../../../x.mp4",
        "tracks/./screen.mp4",
        "tracks/screen.mp4 ",
        "\u202Emp4.exe",
        "mix.flac",
        "project.json",
        string.Empty,
        new string('a', 70_000),
        -1,
        long.MaxValue,
        long.MinValue,
        1e308,
        true,
        new JsonArray(),
        new JsonObject(),
        new JsonArray(new JsonArray(new JsonArray(new JsonArray(new JsonArray())))),
    ];

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task MutatedVideoEntriesLoadSafelyOrAreRefused()
    {
        var library = new FakeLibraryLocation(_directory.File("Library"));
        var store = new ProjectStore(library, TimeProvider.System, NullLogger<ProjectStore>.Instance);
        var created = await store.CreateAsync(new ProjectCreateRequest("Fuzz", "meeting", DateTimeOffset.UnixEpoch, ProjectStates.Ready), CancellationToken.None);
        var seeded = await store.SaveAsync(created with { HasVideo = true, VideoTracks = [Track("screen", "display"), Track("camera", "camera")] }, CancellationToken.None);
        var folder = store.GetProjectFolder(seeded.Id);
        var manifestPath = Path.Combine(folder, ProjectLayout.ManifestFile);
        var v4 = await File.ReadAllTextAsync(manifestPath);
        var v3Node = JsonNode.Parse(v4)!.AsObject();
        v3Node["schemaVersion"] = 3;
        string[] seeds = [v4, v3Node.ToJsonString()];

        var random = new Random(20261011);
        var failures = new List<string>();
        var (loaded, refused) = (0, 0);
        for (var i = 0; i < Iterations; i++)
        {
            var seed = seeds[i % seeds.Length];
            var (text, description) = random.Next(2) == 0 ? MutateField(seed, random) : MutateBytes(seed, random);
            await File.WriteAllTextAsync(manifestPath, text);
            try
            {
                var manifest = await store.LoadAsync(seeded.Id, CancellationToken.None);
                loaded++;
                if (ProjectPaths.FirstUnsafe(manifest) is { } field)
                {
                    failures.Add($"#{i} ({description}): loaded with an unsafe {field}");
                }

                foreach (var (file, _) in VideoRemovalService.VideoFiles(manifest, folder))
                {
                    if (!VideoRemovalService.IsVideoFile(file))
                    {
                        failures.Add($"#{i} ({description}): Remove video would delete {file}");
                    }
                }
            }
            catch (ProjectNotFoundException)
            {
                refused++;
            }
            catch (Exception e)
            {
                failures.Add($"#{i} ({description}): {e.GetType().Name}: {e.Message} {e.StackTrace?.Split('\n')[0].Trim()}");
            }
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Iterations} runs: {loaded} loaded, {refused} refused, {failures.Count} failures"));
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(20)));
    }

    private static VideoTrack Track(string id, string kind) => new()
    {
        Id = id,
        Role = id,
        SourceKind = kind,
        SourceId = kind + ":1",
        Name = "Display 1",
        File = $"tracks/{id}.mp4",
        Parts = [new VideoTrackPart($"tracks/{id}.mp4", 700, 20_000, "Encoder", true, 600), new VideoTrackPart($"tracks/{id}.part2.mp4", 20_700, 5_000, "Software", false, 150)],
        Width = 1920,
        Height = 1080,
        FrameRate = 30,
        StartOffsetMs = 700,
        DurationMs = 25_000,
        FramesWritten = 750,
    };

    private static (string Text, string Description) MutateBytes(string seed, Random random)
    {
        var (bytes, description) = ByteMutator.Mutate(Encoding.UTF8.GetBytes(seed), random);
        return (Encoding.UTF8.GetString(bytes), description);
    }

    /// <summary>Sets one field of one video track or part (or the array itself) to a hostile value, or duplicates a key.</summary>
    private static (string Text, string Description) MutateField(string seed, Random random)
    {
        var root = JsonNode.Parse(seed)!.AsObject();
        var tracks = root["videoTracks"]!.AsArray();
        var track = tracks[random.Next(tracks.Count)]!.AsObject();
        var value = HostileValues[random.Next(HostileValues.Length)]?.DeepClone();
        switch (random.Next(5))
        {
            case 0:
            {
                var key = track.Select(p => p.Key).ElementAt(random.Next(track.Count));
                track[key] = value;
                return (root.ToJsonString(), $"videoTracks[].{key} = {Show(value)}");
            }

            case 1:
            {
                var parts = track["parts"]!.AsArray();
                var part = parts[random.Next(parts.Count)]!.AsObject();
                var key = part.Select(p => p.Key).ElementAt(random.Next(part.Count));
                part[key] = value;
                return (root.ToJsonString(), $"videoTracks[].parts[].{key} = {Show(value)}");
            }

            case 2:
                root["videoTracks"] = value;
                return (root.ToJsonString(), $"videoTracks = {Show(value)}");

            case 3:
                tracks.Add(value);
                return (root.ToJsonString(), $"videoTracks += {Show(value)}");

            default:
            {
                // A key written twice (JsonObject refuses it when read).
                var text = root.ToJsonString();
                var at = text.IndexOf("\"file\":", StringComparison.Ordinal);
                return (text.Insert(at, "\"file\":\"../x.mp4\","), "a duplicated file key");
            }
        }
    }

    private static string Show(JsonNode? value)
    {
        var text = value?.ToJsonString() ?? "null";
        return text.Length > 40 ? text[..40] + "…" : text;
    }
}
