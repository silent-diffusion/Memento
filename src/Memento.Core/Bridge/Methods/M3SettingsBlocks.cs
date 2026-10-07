using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Secrets;
using Memento.Core.Settings;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// The M3 settings blocks (<c>general</c>, <c>export</c>, <c>ai</c>, <c>storage</c>; BRIDGE.md "Settings snapshot (M3
/// additions)") for <c>settings.get</c> and <c>settings.set</c>. Each block changes only the fields it carries;
/// <c>export.defaults</c> replaces whole. The read side reports <c>ai.providers.*.hasKey</c>, never a key.
/// </summary>
internal static class M3SettingsBlocks
{
    public static SettingsSnapshot Complete(SettingsSnapshot snapshot, AppSettings settings, SettingsExtras? extras)
    {
        var general = settings.General;
        var export = settings.Export;
        var ai = settings.Ai;
        var share = ai.Share;
        return snapshot with
        {
            General = new GeneralSettingsSnapshot(
                SafeStartup(extras) ?? general.StartWithWindows,
                general.KeepRunningInTray,
                general.Language),
            Export = new ExportSettingsSnapshot(export.SaveCopiesOutside, export.DefaultFolder, export.AskWhereEachTime, export.CreateSubfolder, export.Defaults),
            Ai = new AiSettingsSnapshot(
                ai.Enabled,
                ai.AskBeforeSend,
                ai.KeepRecord,
                new AiShareSnapshot(share.Transcript, share.Details, share.Participants, share.Agenda, share.Highlights, share.Attachments),
                new AiProvidersSnapshot(
                    new AiProviderKeyState(extras?.Secrets.HasKey(AiProviders.Anthropic) ?? false),
                    new AiProviderKeyState(extras?.Secrets.HasKey(AiProviders.OpenAi) ?? false))),
            Storage = new LibraryStorageSettingsSnapshot(settings.Storage.ReclaimOlderThanDays),
        };
    }

    /// <summary>Applies the M3 blocks of <paramref name="parameters"/> to <paramref name="current"/>.</summary>
    /// <exception cref="BridgeException"><c>settings.invalidValue</c> for a value of the wrong kind.</exception>
    public static AppSettings Merge(AppSettings current, SettingsSetParams parameters)
    {
        var next = current;
        if (parameters.General is { } general)
        {
            next = next with
            {
                General = next.General with
                {
                    StartWithWindows = general.StartWithWindows ?? next.General.StartWithWindows,
                    KeepRunningInTray = general.KeepRunningInTray ?? next.General.KeepRunningInTray,
                    Language = general.Language?.Trim() ?? next.General.Language,
                },
            };
        }

        if (parameters.Export is { } export)
        {
            next = next with
            {
                Export = next.Export with
                {
                    SaveCopiesOutside = export.SaveCopiesOutside ?? next.Export.SaveCopiesOutside,
                    DefaultFolder = NullableString(export.DefaultFolder, "export.defaultFolder", next.Export.DefaultFolder),
                    AskWhereEachTime = export.AskWhereEachTime ?? next.Export.AskWhereEachTime,
                    CreateSubfolder = export.CreateSubfolder ?? next.Export.CreateSubfolder,
                    Defaults = export.Defaults ?? next.Export.Defaults,
                },
            };
        }

        if (parameters.Ai is { } ai)
        {
            var share = next.Ai.Share;
            if (ai.Share is { } patch)
            {
                share = share with
                {
                    Transcript = patch.Transcript ?? share.Transcript,
                    Details = patch.Details ?? share.Details,
                    Participants = patch.Participants ?? share.Participants,
                    Agenda = patch.Agenda ?? share.Agenda,
                    Highlights = patch.Highlights ?? share.Highlights,
                    Attachments = patch.Attachments ?? share.Attachments,
                };
            }

            next = next with
            {
                Ai = next.Ai with
                {
                    Enabled = ai.Enabled ?? next.Ai.Enabled,
                    AskBeforeSend = ai.AskBeforeSend ?? next.Ai.AskBeforeSend,
                    KeepRecord = ai.KeepRecord ?? next.Ai.KeepRecord,
                    Share = share,
                },
            };
        }

        if (parameters.Storage is { } storage)
        {
            next = next with { Storage = next.Storage with { ReclaimOlderThanDays = NullableDays(storage.ReclaimOlderThanDays, next.Storage.ReclaimOlderThanDays) } };
        }

        return next;
    }

    /// <summary>The first problem with the M3 blocks, worded for people, or <c>null</c>.</summary>
    public static string? Validate(AppSettings settings) =>
        settings.General.Validate() ?? settings.Export.Validate() ?? settings.Storage.Validate();

    /// <summary>
    /// Changes the Windows startup entry when <c>general.startWithWindows</c> is sent, before the settings are written,
    /// so a refusal changes nothing.
    /// </summary>
    public static void ApplyStartup(SettingsSetParams parameters, SettingsExtras? extras)
    {
        if (parameters.General?.StartWithWindows is { } enabled && extras is not null && extras.Startup.IsEnabled != enabled)
        {
            AppSetStartupMethod.Apply(extras.Startup, enabled);
        }
    }

    private static bool? SafeStartup(SettingsExtras? extras)
    {
        try
        {
            return extras?.Startup.IsEnabled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? NullableString(JsonElement element, string field, string? current) => element.ValueKind switch
    {
        JsonValueKind.Undefined => current,
        JsonValueKind.Null => null,
        JsonValueKind.String => string.IsNullOrWhiteSpace(element.GetString()) ? null : element.GetString()!.Trim(),
        _ => throw new BridgeException(DomainErrorCodes.SettingsInvalidValue, $"{field} must be a folder path or null. Nothing was changed.", field),
    };

    private static int? NullableDays(JsonElement element, int? current) => element.ValueKind switch
    {
        JsonValueKind.Undefined => current,
        JsonValueKind.Null => null,
        JsonValueKind.Number when element.TryGetInt32(out var days) => days,
        _ => throw new BridgeException(
            DomainErrorCodes.SettingsInvalidValue,
            $"storage.reclaimOlderThanDays must be a whole number of days from {LibraryStorageSettings.MinDays} to {LibraryStorageSettings.MaxDays}, or null. Nothing was changed.",
            "storage.reclaimOlderThanDays"),
    };
}
