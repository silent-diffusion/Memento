using System.Globalization;
using Memento.AI;
using Memento.AI.Local;
using Memento.Core.Ai;
using Memento.Core.Engines;
using Memento.Core.Formatting;
using Memento.Core.Models;
using Memento.Core.Settings;

namespace Memento.Generation.Ai;

/// <summary>
/// The three providers (ARCHITECTURE.md §8): Claude and ChatGPT with keys from the DPAPI store and the model from
/// Settings, and the local model from the catalog run in Memento.Worker. Readiness (<c>providers.list</c>) is worked out
/// without constructing a provider: external AI allowed and a key saved for the cloud ones; for the local one, an
/// installed model (<see cref="LocalModelChoice.EffectiveId"/>), so Local is ready whenever any local model is installed.
/// A graphics-card model without room on the card gives way to an installed processor model, or else runs on the
/// processor itself; the status names the model, where it runs and why. Processor jobs are sent with <c>device: cpu</c>,
/// which loads llama.cpp's CPU build.
/// </summary>
public sealed class ProviderRegistry(ISettingsStore settings, ISecretReader secrets, IModelManager models, IResourceProbe probe, IAiProviderFactory factory)
{
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
        _ => CloudStatus(id),
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

        var plan = status.Plan!;
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
        var model = configured ?? (id == ProviderIds.Anthropic ? AiModelDefaults.AnthropicModel : AiModelDefaults.OpenAiModel);
        if (!settings.Current.Ai.Enabled)
        {
            return new ProviderStatus(id, false, Core.Bridge.DomainErrorCodes.AiDisabled, "External AI is off",
                $"External AI is off, so nothing is sent to {name}. Turn on \"Allow external AI services\" in Settings › AI and privacy.", model, model);
        }

        if (!secrets.HasKey(id))
        {
            return new ProviderStatus(id, false, AiErrorCodes.NoKey, "No key saved", AiErrors.NoKey(name).Message, model, model);
        }

        return new ProviderStatus(id, true, null, null, $"Sends the ticked inputs to {name} ({model}) when you press Generate.", model, model);
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
            return new ProviderStatus(ProviderIds.Local, false, AiErrorCodes.ModelNotInstalled, "Model not installed",
                AiErrors.ModelNotInstalled(LocalAiProvider.ProviderName, entry.Name).Message, entry.Id, entry.Name, entry);
        }

        // A model chosen in Settings that is not installed any more: say which one stands in. The hardware's recommendation
        // is not named when it is missing; the installed model simply writes.
        var notes = new List<string>();
        if (saved is not null && saved != entry.Id && catalog.Find(saved) is { Kind: ModelKinds.Llm } missing && !models.IsInstalled(saved))
        {
            notes.Add($"{missing.Name}, chosen in Settings, is not installed, so {entry.Name} writes the documents.");
        }

        var free = snapshot.DiscreteGpu?.FreeVramBytes;
        var plan = LocalVramPlanner.Plan(entry.Llm, LocalLlmDevices.Auto, free, 0, VramMarginBytes);
        if (!plan.UseGpu && entry.RunsOn == "gpu")
        {
            // A graphics-card model that does not fit now: an installed processor model runs instead, else this one runs on the processor.
            var standIn = catalog.OfKind(ModelKinds.Llm)
                .Where(m => m.Id != entry.Id && m.RunsOn != "gpu" && models.IsInstalled(m.Id))
                .Select(LocalModelCatalog.ToLocal)
                .OfType<LocalModelEntry>()
                .FirstOrDefault();
            var tooSmall = free is { } shortBy and > 0
                ? $"The graphics card has {HumanFormat.Bytes(shortBy)} free and {entry.Name} needs {HumanFormat.Bytes(plan.NeededVramBytes)} on it"
                : $"No graphics card memory is free for {entry.Name}";
            if (standIn is not null && models.Resolve(standIn.Id) is { } standInPath)
            {
                notes.Add($"{tooSmall}, so {standIn.Name} writes instead this time.");
                entry = standIn;
                path = standInPath;
                plan = LocalVramPlanner.Plan(entry.Llm, LocalLlmDevices.Auto, free, 0, VramMarginBytes);
            }
            else
            {
                notes.Add($"{tooSmall}, so it runs on the processor, which takes several times longer.");
            }
        }

        var where = plan.UseGpu ? "graphics card" : "processor";
        var k = plan.ContextTokens / 1024;
        var article = k is 8 or 11 or 18 or (>= 80 and < 90) ? "an" : "a";
        notes.Add(string.Create(CultureInfo.InvariantCulture, $"Runs on the {where} with {article} {k}k context. Nothing leaves this PC."));
        return new ProviderStatus(ProviderIds.Local, true, null, null, string.Join(' ', notes), entry.Id, $"{entry.Name} · {where}", entry, path, plan);
    }
}
