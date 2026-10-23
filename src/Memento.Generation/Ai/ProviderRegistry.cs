using System.Globalization;
using Memento.AI;
using Memento.AI.Local;
using Memento.AI.Server;
using Memento.Core.Ai;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Formatting;
using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Generation.Ai;

/// <summary>
/// The four providers (ARCHITECTURE.md §8): Claude and ChatGPT with keys from the DPAPI store and the model from
/// Settings, "Your own server" at the address, model and context from Settings (2.5.0), and the local model from the
/// catalog run in Memento.Worker. Readiness (<c>providers.list</c>) is worked out
/// without constructing a provider: external AI allowed and a key saved for the cloud ones; for the local one, an
/// installed model (<see cref="LocalModelChoice.EffectiveId"/>), so Local is ready whenever any local model is installed.
/// A graphics-card model without room on the card gives way to an installed processor model, or else runs on the
/// processor itself; the status names the model, where it runs and why. Processor jobs are sent with <c>device: cpu</c>,
/// which loads llama.cpp's CPU build.
/// </summary>
/// <remarks>
/// A larger model (a catalog <c>llm</c> profile that can be split) is planned by <see cref="MemoryPlanner"/> from the card's
/// free memory, the RAM available and what else Memento needs now (<see cref="IMemoryDemands"/>), corrected by its last
/// measured load (<see cref="LocalLoadHistory"/>); one that does not fit is not ready (<c>ai.notEnoughMemory</c>) and no
/// other model stands in for it.
/// </remarks>
public sealed class ProviderRegistry(
    ISettingsStore settings,
    ISecretReader secrets,
    IModelManager models,
    IResourceProbe probe,
    IAiProviderFactory factory,
    IMemoryDemands? demands = null,
    LocalLoadHistory? loads = null)
{
    /// <summary>What the larger models keep free (ENGINE-NOTES.md §T).</summary>
    public static MemoryHeadroom Headroom => MemoryHeadroom.Default;

    /// <summary>Kept free on the graphics card beyond the model's estimate.</summary>
    public const long VramMarginBytes = 256L * 1024 * 1024;

    /// <summary>The provider in effect: the template's, else the Settings default, else the local model (nothing leaves the PC).</summary>
    public string ResolveId(string? templateProviderId) =>
        ProviderIds.IsValid(templateProviderId) ? templateProviderId!
        : ProviderIds.IsValid(settings.Current.Ai.DefaultProviderId) ? settings.Current.Ai.DefaultProviderId!
        : ProviderIds.Local;

    public IReadOnlyList<ProviderStatus> List() => ProviderIds.All.Select(Status).ToList();

    public ProviderStatus Status(string id) => id switch
    {
        ProviderIds.Local => LocalStatus(),
        ProviderIds.Server => ServerStatus(),
        _ => CloudStatus(id),
    };

    /// <summary>
    /// Whether a generation with <paramref name="id"/> would take the inputs off this PC, from Settings alone (no provider,
    /// no file read, no lookup): the privacy check that comes before anything else in <c>generation.start</c>.
    /// </summary>
    public bool LeavesPc(string id) => id switch
    {
        ProviderIds.Local => false,
        ProviderIds.Server => settings.Current.Ai.Server.ParsedAddress()?.LeavesPc ?? false,
        _ => true,
    };

    /// <summary>Constructs the provider for a generation. Only call it with a ready status.</summary>
    public IAiProvider Create(ProviderStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!status.Ready)
        {
            throw new InvalidOperationException($"The provider {status.Id} is not ready ({status.Code}).");
        }

        if (status.IsCloud)
        {
            return factory.CreateCloud(status.Id, status.Model!);
        }

        if (status.IsServer)
        {
            return factory.CreateServer(new ServerOptions(status.ServerAddress!, status.Model!, status.ServerContextTokens));
        }

        var plan = status.Plan!;
        if (status.MemoryPlan is { Fits: true } memory)
        {
            return factory.CreateLocal(status.LocalModel!, status.LocalPath!, LargerOptions(memory, demands?.Current() ?? MemoryDemands.None), FreeVram);
        }

        return factory.CreateLocal(
            status.LocalModel!,
            status.LocalPath!,
            new LocalAiOptions
            {
                // The CPU build is faster on the processor than the Vulkan build with no layers (ENGINE-NOTES.md §I).
                Device = plan.UseGpu ? LocalLlmDevices.Gpu : LocalLlmDevices.Cpu,
                ContextTokens = plan.ContextTokens,
                VramMarginBytes = VramMarginBytes,
            },
            FreeVram);
    }

    private long? FreeVram() => probe.Sample().DiscreteGpu?.FreeVramBytes;

    private ProviderStatus CloudStatus(string id)
    {
        var name = ProviderIds.DisplayName(id);
        var configured = id == ProviderIds.Anthropic ? settings.Current.Ai.Providers.Anthropic.Model : settings.Current.Ai.Providers.Openai.Model;
        var cloud = AiModelDefaults.Describe(id, AiModelDefaults.Effective(id, configured));
        var model = cloud.Id;
        if (!settings.Current.Ai.Enabled)
        {
            return new ProviderStatus(id, false, Core.Bridge.DomainErrorCodes.AiDisabled, "External AI is off",
                $"External AI is off, so nothing is sent to {name}. Turn on \"Allow external AI services\" in Settings › AI and privacy.", model, model) { Cloud = cloud };
        }

        if (!secrets.HasKey(id))
        {
            return new ProviderStatus(id, false, AiErrorCodes.NoKey, "No key saved", AiErrors.NoKey(name).Message, model, model) { Cloud = cloud };
        }

        var status = new ProviderStatus(id, true, null, null, null, model, model) { Cloud = cloud };
        return status with { Detail = CloudDetail(name, cloud, status.ChunkTokens) };
    }

    /// <summary>
    /// "Your own server" (2.5.0), from Settings only (no request, no lookup): no address is <c>ai.noServer</c>; an address
    /// that leaves the PC while external AI is off is <c>ai.disabled</c>; plain http outside this PC and the local network is
    /// <c>ai.serverInsecure</c>; no model is <c>ai.noModel</c>. A ready server names the model, the address and where the
    /// inputs go. Whether the server answers is "Test connection"'s to say, or the generation's.
    /// </summary>
    private ProviderStatus ServerStatus()
    {
        const string Id = ProviderIds.Server;
        var name = ProviderIds.DisplayName(Id);
        var server = settings.Current.Ai.Server;
        var address = server.ParsedAddress();
        var model = server.Model;
        var context = AiServerSettings.ClampContext(server.ContextTokens);
        if (address is null)
        {
            return new ProviderStatus(Id, false, DomainErrorCodes.AiNoServer, "No server address",
                "No server address is set, so nothing is sent to Your own server. Enter the address of Ollama, LM Studio or another OpenAI-compatible server in Settings › AI and privacy.", model, null)
            {
                ServerContextTokens = context,
            };
        }

        var label = model is null ? address.HostAndPort : $"{model} at {address.HostAndPort}";
        ProviderStatus Status(bool ready, string? code, string? reason, string? detail) =>
            new(Id, ready, code, reason, detail, model, label) { ServerAddress = address, ServerContextTokens = context };

        if (address.LeavesPc && !settings.Current.Ai.Enabled)
        {
            return Status(false, Core.Bridge.DomainErrorCodes.AiDisabled, "External AI is off",
                $"The server at {address.Text} is not on this PC, so with external AI off nothing is sent to it. Turn on \"Allow external AI services\" in Settings › AI and privacy, or use a server on this PC.");
        }

        if (address.IsInsecure)
        {
            return Status(false, DomainErrorCodes.AiServerInsecure, "Needs https", AiErrors.ServerInsecure(name, address.Text).Message);
        }

        if (model is null)
        {
            return Status(false, DomainErrorCodes.AiNoModel, "No model chosen",
                $"No model is chosen for the server at {address.Text}, so nothing is sent to it. Choose one in Settings › AI and privacy (Test connection lists the server's models).");
        }

        var status = Status(true, null, null, null);
        return status with { Detail = ServerDetail(address, model, context, status.ChunkTokens) };
    }

    /// <summary>The ready card's note for "Your own server": where the inputs go, the context Memento assumes, and the split.</summary>
    internal static string ServerDetail(ServerAddress address, string model, int contextTokens, int chunkTokens)
    {
        var where = address.Zone switch
        {
            ServerZone.ThisPc => "The server runs on this PC, so nothing leaves it.",
            ServerZone.Network => "The server is on your network, so the inputs leave this PC.",
            _ => "The server is outside your network, so the inputs leave this PC.",
        };
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Sends the ticked inputs to {model} at {address.Text} when you press Generate. {where} Memento assumes the server gives the model {contextTokens:N0} tokens of context, as set in Settings (Ollama gives 4,096 unless num_ctx was raised); a long transcript is split into segments of up to {chunkTokens:N0} tokens.");
    }

    /// <summary>
    /// The ready card's note: where the inputs go, how much the model reads, and what Memento does with a long transcript
    /// (it is split, so no transcript is refused for its length); for a model typed by hand, what Memento assumes.
    /// </summary>
    internal static string CloudDetail(string name, CloudModel cloud, int chunkTokens)
    {
        var size = cloud.Listed
            ? $"{cloud.Id} reads up to {AiModelDefaults.Tokens(cloud.ContextTokens)} tokens a request"
            : $"{cloud.Id} is not in Memento's list, so Memento assumes it reads up to {AiModelDefaults.Tokens(cloud.ContextTokens)} tokens a request and sends it no effort setting";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Sends the ticked inputs to {name} ({cloud.Id}) when you press Generate. {size}; a long transcript is split into segments of up to {chunkTokens:N0} tokens, so it always fits.");
    }

    /// <summary>
    /// The worker's options for a larger model's plan: its device, context, threads and placement, the RAM the worker
    /// checks is still available before loading (Windows, the app and a recording being captured), and flash attention
    /// on the card (it halves a split load's compute buffer, ENGINE-NOTES.md §T).
    /// </summary>
    public static LocalAiOptions LargerOptions(LocalMemoryPlan plan, MemoryDemands demands)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(demands);
        var reserve = Headroom.OsBytes + Headroom.AppBytes + (demands.Recording ? Headroom.CaptureBytes + (demands.RecordingVideo ? Headroom.CaptureVideoBytes : 0) : 0);
        return new LocalAiOptions
        {
            Device = plan.Device,
            ContextTokens = plan.ContextTokens,
            Threads = plan.Threads,
            VramMarginBytes = Headroom.VramMarginBytes,
            Placement = LocalLlmPlacement.From(plan, Headroom, reserve),
            FlashAttention = plan.UsesCard ? true : null,
        };
    }

    /// <summary>
    /// The plan of every larger local model for this PC now (Settings' "Larger models" group shows each one's line before
    /// it is first used), installed or not.
    /// </summary>
    public IReadOnlyList<LocalModelPlanInfo> LargerModelPlans()
    {
        var snapshot = probe.Sample();
        var demand = demands?.Current() ?? MemoryDemands.None;
        return models.Catalog.OfKind(ModelKinds.Llm)
            .Select(LocalModelCatalog.ToLocal)
            .OfType<LocalModelEntry>()
            .Where(m => m.Llm.CanSplit)
            .Select(m => PlanInfo(m, MemoryPlanner.Plan(m.Llm, MemoryPlanInput.From(snapshot, demand), loads?.Last(m.Id), Headroom)))
            .ToList();
    }

    /// <summary>A plan as Settings and the Builder show it.</summary>
    public static LocalModelPlanInfo PlanInfo(LocalModelEntry model, LocalMemoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        return new LocalModelPlanInfo(
            model.Id,
            plan.Fits,
            plan.Fits ? MemoryPlanWording.Line(plan) : MemoryPlanWording.Refusal(model.Name, plan),
            plan.Placement,
            plan.GpuLayers,
            plan.CardBlocks,
            plan.TotalLayers,
            plan.CpuExpertLayers,
            plan.ContextTokens,
            plan.VramBytes,
            plan.RamBytes,
            plan.TokensPerSecond);
    }

    private ProviderStatus LocalStatus()
    {
        var snapshot = probe.Sample();
        var saved = settings.Current.Ai.LocalModelId;
        var catalog = models.Catalog;
        var modelId = LocalModelChoice.EffectiveId(saved, catalog, snapshot, models.IsInstalled);
        var entry = modelId is null ? null : catalog.Find(modelId) is { } found ? LocalModelCatalog.ToLocal(found) : null;
        if (entry is null)
        {
            return new ProviderStatus(ProviderIds.Local, false, AiErrorCodes.ModelNotInstalled, "Model not installed",
                "No local model is available in this build. Choose Claude or ChatGPT instead.", null, null);
        }

        var path = models.Resolve(entry.Id);
        if (path is null)
        {
            // The card is still described: what is free helps choose which model to download.
            return new ProviderStatus(ProviderIds.Local, false, AiErrorCodes.ModelNotInstalled, "Model not installed",
                AiErrors.ModelNotInstalled(LocalAiProvider.ProviderName, entry.Name).Message, entry.Id, entry.Name, entry)
            {
                GpuMemory = snapshot.DiscreteGpu is { } card ? GpuMemoryInfo.From(card) : null,
            };
        }

        // A model chosen in Settings that is not installed any more: say which one stands in. The hardware's recommendation
        // is not named when it is missing; the installed model simply writes.
        var notes = new List<string>();
        if (saved is not null && saved != entry.Id && catalog.Find(saved) is { Kind: ModelKinds.Llm } missing && !models.IsInstalled(saved))
        {
            notes.Add($"{missing.Name}, chosen in Settings, is not installed, so {entry.Name} writes the documents.");
        }

        var gpu = snapshot.DiscreteGpu;
        var free = gpu?.FreeVramBytes;
        if (entry.Llm.CanSplit)
        {
            return LargerStatus(entry, path, snapshot, notes);
        }

        var plan = LocalVramPlanner.Plan(entry.Llm, LocalLlmDevices.Auto, free, 0, VramMarginBytes);
        string? gpuNote = null;
        if (!plan.UseGpu && entry.RunsOn == "gpu")
        {
            // A graphics-card model that does not fit now: an installed processor model runs instead, else this one runs on the processor.
            var standIn = catalog.OfKind(ModelKinds.Llm)
                .Where(m => m.Id != entry.Id && m.RunsOn != "gpu" && models.IsInstalled(m.Id))
                .Select(LocalModelCatalog.ToLocal)
                .OfType<LocalModelEntry>()
                .FirstOrDefault();
            var standInPath = standIn is null ? null : models.Resolve(standIn.Id);
            var instead = standInPath is not null ? $"{standIn!.Name} writes instead" : "it runs on the processor (several times slower)";

            // The card's free memory, who holds the rest and the fix (DESIGN.md §17); without a card, only that there is none.
            gpuNote = gpu is not null
                ? GpuMemoryWording.Shortfall(gpu, entry.Name, plan.NeededVramBytes, instead)
                : $"No graphics card memory is free for {entry.Name}, so {instead}.";
            notes.Add(gpuNote);
            if (standInPath is not null)
            {
                entry = standIn!;
                path = standInPath;
                plan = LocalVramPlanner.Plan(entry.Llm, LocalLlmDevices.Auto, free, 0, VramMarginBytes);
            }
        }

        var where = plan.UseGpu ? "graphics card" : "processor";
        var k = plan.ContextTokens / 1024;
        var article = k is 8 or 11 or 18 or (>= 80 and < 90) ? "an" : "a";
        notes.Add(string.Create(CultureInfo.InvariantCulture, $"Runs on the {where} with {article} {k}k context. Nothing leaves this PC."));
        return new ProviderStatus(ProviderIds.Local, true, null, null, string.Join(' ', notes), entry.Id, $"{entry.Name} · {where}", entry, path, plan)
        {
            GpuMemory = gpu is null ? null : GpuMemoryInfo.From(gpu),
            GpuNote = gpuNote,
        };
    }

    /// <summary>
    /// A larger model: placed by <see cref="MemoryPlanner"/>. One that does not fit is not ready, with the §17 sentence
    /// (no smaller model stands in: it was chosen for its size); one that fits names its placement and the plan's line.
    /// </summary>
    private ProviderStatus LargerStatus(LocalModelEntry entry, string path, ResourceSnapshot snapshot, List<string> notes)
    {
        var demand = demands?.Current() ?? MemoryDemands.None;
        var memory = MemoryPlanner.Plan(entry.Llm, MemoryPlanInput.From(snapshot, demand), loads?.Last(entry.Id), Headroom);
        var info = PlanInfo(entry, memory);
        var gpu = snapshot.DiscreteGpu;
        var plan = new LocalLlmPlan(memory.UsesCard, memory.Fits, memory.GpuLayers, memory.ContextTokens, memory.VramBytes + Headroom.VramMarginBytes, memory.Reason);
        if (!memory.Fits)
        {
            notes.Insert(0, info.Line);
            return new ProviderStatus(ProviderIds.Local, false, AiErrorCodes.NotEnoughMemory, "Not enough memory", string.Join(' ', notes), entry.Id, entry.Name, entry, path, plan)
            {
                GpuMemory = gpu is null ? null : GpuMemoryInfo.From(gpu),
                MemoryPlan = memory,
                PlanInfo = info,
            };
        }

        var k = memory.ContextTokens / 1024;
        var article = k is 8 or 11 or 18 or (>= 80 and < 90) ? "an" : "a";
        notes.Add(string.Create(CultureInfo.InvariantCulture, $"{info.Line}, with {article} {k}k context. Nothing leaves this PC."));
        return new ProviderStatus(ProviderIds.Local, true, null, null, string.Join(' ', notes), entry.Id, $"{entry.Name} · {MemoryPlanWording.Where(memory)}", entry, path, plan)
        {
            GpuMemory = gpu is null ? null : GpuMemoryInfo.From(gpu),
            MemoryPlan = memory,
            PlanInfo = info,
        };
    }
}
