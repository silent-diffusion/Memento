using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Projects;
using Memento.Core.Secrets;
using Memento.Core.Serialization;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Memento.Core.Voices;
using Xunit.Abstractions;

namespace Memento.Core.Tests.Serialization;

/// <summary>
/// Security audit 2, §9: System.Text.Json on .NET 8 reads a missing <c>init</c> member or constructor parameter as
/// <c>null</c> (or 0, or false) whatever its annotation says (the 2.1.1/2.1.2 settings bugs). Every list and object
/// member of every type Memento.Core reads from disk must keep its default when the JSON leaves it out (an initializer
/// and a setter, or <c>?? []</c> on a positional record), and every initializer must survive. Parameters from the page
/// either keep their defaults or are refused by name at the bridge. A new member that does neither fails here.
/// </summary>
public sealed class NonNullableMembersReadFromEmptyObjectsTests(ITestOutputHelper output)
{
    /// <summary>Members with no default on purpose: the loader refuses the file by name (see NullMemberRepairTests).</summary>
    private static readonly HashSet<string> RefusedByTheLoader = new(StringComparer.Ordinal)
    {
        // A version without its transcript cannot be restored; it is listed as unreadable.
        $"{typeof(TranscriptVersionFile).FullName}:transcript",

        // Review falls back to talk time when voices.json has no clusters or tracks (as before 2.4.0).
        $"{typeof(VoicesDocument).FullName}:tracks",
        $"{typeof(VoicesDocument).FullName}:clusters",
    };

    public static TheoryData<string> FileContexts => [.. FilesByName.Keys];

    public static TheoryData<string> BridgeContexts => [.. BridgeByName.Keys];

    /// <summary>The contexts of the files Memento.Core reads from disk.</summary>
    private static Dictionary<string, JsonSerializerContext> FilesByName { get; } = new(StringComparer.Ordinal)
    {
        ["settings.json"] = SettingsJsonContext.Default,
        ["project.json, annotations.json, history.jsonl, recording.state.json, folders.json"] = ProjectJsonContext.Default,
        ["transcript.json, voices.json, transcript versions and partials"] = TranscriptJsonContext.Default,
        ["voices/known.json"] = KnownVoicesJsonContext.Default,
        ["catalog.json, model stamps"] = ModelJsonContext.Default,
        ["export manifest.json and journal"] = ExportJsonContext.Default,
        ["secrets.bin"] = SecretsJsonContext.Default,
    };

    /// <summary>The bridge contexts; only what the page sends (<c>*Params</c> and the request envelope) is read.</summary>
    private static Dictionary<string, JsonSerializerContext> BridgeByName { get; } = new(StringComparer.Ordinal)
    {
        ["bridge"] = BridgeJsonContext.Default,
        ["bridge.library"] = LibraryBridgeJsonContext.Default,
        ["bridge.live"] = LiveBridgeJsonContext.Default,
        ["bridge.m3"] = M3BridgeJsonContext.Default,
        ["bridge.m4"] = M4BridgeJsonContext.Default,
        ["bridge.review"] = ReviewBridgeJsonContext.Default,
        ["bridge.updates"] = UpdatesBridgeJsonContext.Default,
        ["bridge.video"] = VideoBridgeJsonContext.Default,
    };

    [Theory]
    [MemberData(nameof(FileContexts))]
    public void NoListOrObjectOfAFileReadsAsNullWhenTheJsonLeavesItOut(string file)
    {
        var types = NonNullableContracts.ObjectTypes(FilesByName[file]);
        output.WriteLine($"{file}: {types.Count} types, {NonNullableContracts.GuardedMemberCount(types)} list and object members");
        var problems = NonNullableContracts.FindNullsReadFromEmptyObjects(FilesByName[file], validated: RefusedByTheLoader);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [MemberData(nameof(BridgeContexts))]
    public async Task EveryParameterTypeKeepsItsDefaultsOrIsRefusedByNameWhenThePageLeavesThemOut(string name)
    {
        var context = BridgeByName[name];
        var problems = new List<string>();
        var walked = NonNullableContracts.ObjectTypes(context, IsSentByThePage);
        var refused = 0;
        output.WriteLine($"{name}: {walked.Count} types, {NonNullableContracts.GuardedMemberCount(walked)} list and object members");
        foreach (var type in walked.Where(IsSentByThePage))
        {
            var json = NonNullableContracts.MinimalJson(context, type);
            var method = (IProbe)Activator.CreateInstance(typeof(Probe<>).MakeGenericType(type), context.GetTypeInfo(type))!;
            using var document = JsonDocument.Parse(json);
            try
            {
                await method.HandleAsync(document.RootElement, CancellationToken.None);
                if (NullMembers.FindFirst(method.Received) is { } path)
                {
                    problems.Add($"{type.Name}: the method was handed a null '{path}' from {json}");
                }

                problems.AddRange(NonNullableContracts.InitializersLost(context, type, method.Received!, json));
            }
            catch (BridgeException ex) when (ex.Code == BridgeErrorCodes.InvalidParams && ex.Message.Contains("is null", StringComparison.Ordinal))
            {
                // Refused by name at the boundary: a member without a default the page must send.
                output.WriteLine($"  {type.Name} without its members is refused: {ex.Message}");
                refused++;
            }
            catch (BridgeException ex)
            {
                problems.Add($"{type.Name}: {json} was refused without naming a member ({ex.Code}: {ex.Message})");
            }
        }

        output.WriteLine($"{refused} parameter types refused when left empty");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryBridgeMethodTakesParametersTheWalkSees()
    {
        // The bridge walk starts from the *Params types; a method taking another type would go unchecked.
        var methods = typeof(BridgeMethod<,>).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && BaseBridgeMethod(t) is not null)
            .ToList();
        Assert.NotEmpty(methods);
        Assert.All(methods, m => Assert.True(IsSentByThePage(BaseBridgeMethod(m)!.GetGenericArguments()[0]), m.FullName));
    }

    [Fact]
    public void TheWalkSeesTheSchemasItGuards()
    {
        // A context whose types the walk cannot see would pass by finding nothing.
        var settings = NonNullableContracts.ObjectTypes(SettingsJsonContext.Default);
        Assert.Contains(typeof(AppSettings), settings);
        Assert.Contains(typeof(VideoSettings), settings);
        var project = NonNullableContracts.ObjectTypes(ProjectJsonContext.Default);
        Assert.Contains(typeof(ProjectManifest), project);
        Assert.Contains(typeof(VideoTrackPart), project);
        Assert.Contains(typeof(FoldersDocument), project);
        var m3 = NonNullableContracts.ObjectTypes(M3BridgeJsonContext.Default, IsSentByThePage);
        Assert.Contains(typeof(AgendaApplyItem), m3);
        Assert.DoesNotContain(typeof(AgendaResult), m3);
    }

    private static bool IsSentByThePage(Type type) =>
        type == typeof(BridgeRequest) || (type.Namespace == typeof(BridgeRequest).Namespace && type.Name.EndsWith("Params", StringComparison.Ordinal));

    private static Type? BaseBridgeMethod(Type type)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(BridgeMethod<,>))
            {
                return t;
            }
        }

        return null;
    }

    private interface IProbe : IBridgeHandler
    {
        object? Received { get; }
    }

    /// <summary>A bridge method that only records what the real <see cref="BridgeMethod{TParams,TResult}"/> boundary hands it.</summary>
    private sealed class Probe<T>(JsonTypeInfo typeInfo) : BridgeMethod<T, EmptyResult>, IProbe
    {
        public override string Name => "test.probe";

        public override JsonTypeInfo<T> ParamsTypeInfo { get; } = (JsonTypeInfo<T>)typeInfo;

        public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

        public object? Received { get; private set; }

        public override Task<EmptyResult> InvokeAsync(T parameters, CancellationToken cancellationToken)
        {
            Received = parameters;
            return Task.FromResult(new EmptyResult());
        }
    }
}
