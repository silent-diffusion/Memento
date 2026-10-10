using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Attachments;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Documents;
using Memento.Core.Projects;
using Memento.Core.Transcripts;
using Memento.Core.Video;

namespace Memento.Core.Export;

/// <summary>
/// Works out the files of an export from the selection and the project as it is now. Text files (transcript,
/// details) are rendered here, so their sizes are exact; audio and attachments are read when the job writes them.
/// Reads only: the project is never changed.
/// </summary>
public sealed class ExportPlanner(IProjectStore store, ProjectService projects, TranscriptStore transcripts, ExportAudio audio, TimeProvider time, IDocumentExportSource? documents = null, IVideoFiles? videoFiles = null)
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public async Task<ExportPlan> PlanAsync(string recordingId, ExportSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ProjectManifest manifest;
        try
        {
            manifest = await store.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }

        var project = await projects.GetAsync(recordingId, cancellationToken);
        var folder = store.GetProjectFolder(recordingId);
        var exportedAt = time.GetLocalNow();
        var baseName = ExportNaming.BaseName(manifest.Details.Title, manifest.CreatedAt);
        var stored = manifest.State is ProjectStates.Ready or ProjectStates.Recovered;
        var items = new List<ExportItem>();
        var unavailable = new List<ExportUnavailable>();
        TranscriptTextOptions? transcriptOptions = null;

        if (selection.AudioMixed.On)
        {
            if (stored && manifest.Mix is { } mix && File.Exists(Full(folder, mix.File)))
            {
                items.Add(AudioItem(ExportComponents.AudioMixed, ExportNaming.MixFile(baseName, selection.AudioMixed.Format), Full(folder, mix.File), mix.Codec, mix.SampleRate, mix.Channels, mix.DurationMs, selection.AudioMixed));
            }
            else
            {
                unavailable.Add(new(ExportComponents.AudioMixed, "Not saved yet"));
            }
        }

        if (selection.Tracks.On)
        {
            var tracks = stored ? manifest.Tracks.Where(t => t.Sha256 is not null && File.Exists(Full(folder, t.File))).ToList() : [];
            if (tracks.Count == 0)
            {
                // 2.0: "Keep only the mix" removed them; otherwise they are not stored yet.
                unavailable.Add(new(ExportComponents.Tracks, manifest.MixOnly is not null ? "Only the mix was kept" : "Not saved yet"));
            }

            foreach (var track in tracks)
            {
                var name = ExportNaming.TrackFile(baseName, track.Name, track.Id, selection.Tracks.Format);
                items.Add(AudioItem(ExportComponents.Tracks, name, Full(folder, track.File), track.Codec, track.SampleRate, track.Channels, track.DurationMs, selection.Tracks));
            }
        }

        if (selection.Video is { On: true } video)
        {
            AddVideo(manifest, folder, baseName, stored, video, items, unavailable);
        }

        if (selection.Transcript.On)
        {
            transcriptOptions = await AddTranscriptAsync(recordingId, folder, selection.Transcript, project, baseName, exportedAt, items, unavailable, cancellationToken);
        }

        if (selection.Documents.On)
        {
            if (documents is null)
            {
                unavailable.Add(new(ExportComponents.Documents, "Documents are not available in this build"));
            }
            else
            {
                var plan = await documents.PlanAsync(recordingId, selection.Documents.DocumentIds ?? [], selection.Documents.Format, cancellationToken);
                if (plan.Unavailable is { } reason)
                {
                    unavailable.Add(new(ExportComponents.Documents, reason));
                }

                items.AddRange(plan.Files.Select(f => new ExportItem(ExportComponents.Documents, baseName + " - " + f.Name, null, f.EstimatedBytes, f.WriteAsync, f.DocumentId)));
            }
        }

        if (selection.Details.On)
        {
            items.Add(TextItem(ExportComponents.Details, ExportNaming.DetailsFile(baseName), null, DetailsJson(project, manifest, exportedAt)));
        }

        if (selection.Attachments.On)
        {
            var files = manifest.Attachments.Where(a => File.Exists(Full(folder, a.File))).ToList();
            if (files.Count == 0)
            {
                unavailable.Add(new(ExportComponents.Attachments, "No attachments"));
            }

            foreach (var attachment in files)
            {
                var path = Full(folder, attachment.File);
                // The name comes from project.json: a plain file name only (no folders, drive or traversal).
                var name = FileNames.SanitizeKeepingExtension(attachment.Name, "Attachment");
                items.Add(new ExportItem(ExportComponents.Attachments, name, ExportNaming.AttachmentsFolder, new FileInfo(path).Length, (destination, ct) => CopyAsync(path, destination, ct)));
            }
        }

        return new ExportPlan(recordingId, manifest.Details.Title, baseName, exportedAt, items, unavailable) { TranscriptOptions = transcriptOptions };
    }

    /// <summary><c>transcript.json</c> as stored, plus when it was exported and which recording it belongs to.</summary>
    internal static string TranscriptJson(string stored, Project project, DateTimeOffset exportedAt)
    {
        var node = JsonNode.Parse(stored, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new InvalidDataException("transcript.json is not a JSON object");
        node["exportedAt"] = JsonValue.Create(exportedAt);
        node["recording"] = new JsonObject
        {
            ["id"] = project.Summary.Id,
            ["title"] = project.Summary.Title,
            ["type"] = project.Summary.Type,
            ["createdAt"] = JsonValue.Create(project.Summary.CreatedAt),
            ["durationMs"] = project.Summary.DurationMs,
            ["details"] = JsonSerializer.SerializeToNode(project.Details, M3BridgeJsonContext.Default.RecordingDetails),
        };
        return ExportJson.Write(node);
    }

    /// <summary>The recording's details, summary, tracks, annotations and History as one JSON file.</summary>
    internal static string DetailsJson(Project project, ProjectManifest manifest, DateTimeOffset exportedAt)
    {
        var body = JsonSerializer.SerializeToNode(project, M3BridgeJsonContext.Default.Project)!.AsObject();

        // Playback URLs only work inside Memento.
        body.Remove("mixUrl");
        body.Remove("peaksUrl");
        var files = new JsonObject();
        foreach (var (file, sha256) in manifest.Integrity.Files.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            files[file] = sha256;
        }

        body.Remove("integrity");
        body["integrity"] = new JsonObject
        {
            ["algorithm"] = manifest.Integrity.Algorithm,
            ["computedAt"] = manifest.Integrity.ComputedAt is { } at ? JsonValue.Create(at) : null,
            ["files"] = files,
        };

        var root = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["exportedAt"] = JsonValue.Create(exportedAt),
            ["recordingId"] = project.Summary.Id,
        };
        foreach (var property in body.ToList())
        {
            body.Remove(property.Key);
            root[property.Key] = property.Value;
        }

        return ExportJson.Write(root);
    }

    /// <summary>
    /// 2.0, the Video row (DESIGN.md §18.4). One file: the screen with the camera inset burnt in where it was while
    /// recording (MP4 re-encodes; "Original container" cannot burn anything in, so it copies the screen's frames and
    /// writes the camera as its own file), with the mixed audio as AAC. Two files: each track at its own resolution;
    /// the mixed audio goes with the screen file. Sizes are estimates: a re-encode lands near the recorded size.
    /// </summary>
    private void AddVideo(ProjectManifest manifest, string folder, string baseName, bool stored, ExportVideoChoice choice, List<ExportItem> items, List<ExportUnavailable> unavailable)
    {
        var tracks = stored && manifest.VideoRemoved is null
            ? manifest.VideoTracks.Where(t => ProjectPaths.IsSafeRelative(t.File) && File.Exists(Full(folder, t.File)) && t.DurationMs > 0).ToList()
            : [];
        if (tracks.Count == 0)
        {
            unavailable.Add(new(
                ExportComponents.Video,
                manifest.VideoRemoved is not null ? "The video was removed" : manifest.VideoTracks.Count == 0 ? "No video was recorded" : stored ? "The video files are missing" : "Not saved yet"));
            return;
        }

        if (videoFiles is not { IsAvailable: true } files)
        {
            unavailable.Add(new(ExportComponents.Video, "Video can't be exported in this copy of Memento"));
            return;
        }

        // The longest track of each role (a role turned off and on again has several; the longest holds the most).
        var screen = tracks.Where(t => t.Role == VideoTrack.RoleScreen).MaxBy(t => t.DurationMs);
        var camera = tracks.Where(t => t.Role == VideoTrack.RoleCamera).MaxBy(t => t.DurationMs);
        var reencode = choice.Format == ExportRules.Mp4;
        var mix = manifest.Mix is { } m && File.Exists(Full(folder, m.File)) ? Full(folder, m.File) : null;
        var duration = Math.Max(manifest.DurationMs, tracks.Max(t => t.StartOffsetMs + t.DurationMs));
        var pip = manifest.VideoPip.Count > 0
            ? manifest.VideoPip.Select(p => new VideoPipChange(p.AtMs, p.Enabled, p.Corner)).ToList()
            : [new VideoPipChange(0, true, VideoPipChange.BottomRight)];
        var audioBytes = mix is null ? 0 : duration * ExportRules.VideoAudioBitrateKbps / 8;

        void Add(string name, VideoTrack main, VideoTrack? inset, bool withAudio)
        {
            var request = new VideoComposeRequest(
                string.Empty,
                Input(folder, main),
                inset is null ? null : Input(folder, inset),
                withAudio ? mix : null,
                withAudio ? duration : main.StartOffsetMs + main.DurationMs,
                reencode,
                pip,
                ExportRules.VideoAudioBitrateKbps);
            var bytes = Processing.VideoStage.SizeOf(folder, main) + (withAudio ? audioBytes : 0) + (inset is null ? 0 : Processing.VideoStage.SizeOf(folder, inset) / 10);
            items.Add(new ExportItem(
                ExportComponents.Video,
                name,
                null,
                bytes,
                (destination, ct) => files.ComposeAsync(request with { OutputPath = destination }, null, ct))
            {
                WriteWithProgressAsync = (destination, progress, ct) => files.ComposeAsync(request with { OutputPath = destination }, progress, ct),
            });
        }

        if (choice.Layout == ExportRules.OneFile && screen is not null)
        {
            Add(ExportNaming.VideoFile(baseName, null), screen, reencode ? camera : null, withAudio: true);
            if (!reencode && camera is not null)
            {
                Add(ExportNaming.VideoFile(baseName, VideoTrack.RoleCamera), camera, null, withAudio: false);
            }
        }
        else if (choice.Layout == ExportRules.OneFile)
        {
            Add(ExportNaming.VideoFile(baseName, null), camera!, null, withAudio: true);
        }
        else
        {
            if (screen is not null)
            {
                Add(ExportNaming.VideoFile(baseName, VideoTrack.RoleScreen), screen, null, withAudio: true);
            }

            if (camera is not null)
            {
                Add(ExportNaming.VideoFile(baseName, VideoTrack.RoleCamera), camera, null, withAudio: screen is null);
            }
        }
    }

    private static VideoComposeInput Input(string folder, VideoTrack track) =>
        new(Full(folder, track.File), track.StartOffsetMs, track.DurationMs, track.Width, track.Height, track.FrameRate, track.BitrateBitsPerSecond);

    /// <summary>A file named by the manifest, never outside the project folder (<see cref="ProjectPaths"/>).</summary>
    private static string Full(string folder, string relative) => ProjectPaths.Resolve(folder, relative);

    private static ExportItem TextItem(string component, string name, string? subfolder, string content)
    {
        var bytes = Utf8.GetBytes(content);
        return new ExportItem(component, name, subfolder, bytes.LongLength, (destination, ct) => File.WriteAllBytesAsync(destination, bytes, ct));
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        // An attachment that came from the internet keeps that mark in the export too.
        await MarkOfTheWeb.CopyAsync(source, destination, cancellationToken);
    }

    private ExportItem AudioItem(string component, string name, string source, string codec, int sampleRate, int channels, long durationMs, ExportAudioChoice choice)
    {
        var bytes = ExportAudio.Estimate(codec, new FileInfo(source).Length, sampleRate, channels, durationMs, choice);
        return new ExportItem(component, name, null, bytes, (destination, ct) => audio.WriteAsync(source, codec, choice, destination, Path.GetDirectoryName(destination)!, ct));
    }

    /// <returns>The text options when a Markdown or text file is written (the manifest records them), else <c>null</c>.</returns>
    private async Task<TranscriptTextOptions?> AddTranscriptAsync(
        string recordingId,
        string folder,
        ExportTranscriptChoice choice,
        Project project,
        string baseName,
        DateTimeOffset exportedAt,
        List<ExportItem> items,
        List<ExportUnavailable> unavailable,
        CancellationToken cancellationToken)
    {
        var formats = (choice.Formats ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (formats.Count == 0)
        {
            unavailable.Add(new(ExportComponents.Transcript, "No format chosen"));
            return null;
        }

        TranscriptDocument? transcript;
        try
        {
            transcript = await transcripts.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectSchemaException)
        {
            unavailable.Add(new(ExportComponents.Transcript, "Its file could not be read"));
            return null;
        }

        if (transcript is null)
        {
            unavailable.Add(new(ExportComponents.Transcript, "Not transcribed yet"));
            return null;
        }

        foreach (var format in ExportRules.TranscriptFormats.Where(formats.Contains))
        {
            var content = format switch
            {
                ExportRules.Markdown or ExportRules.Text => TranscriptText.Format(format, transcript, project.Summary, choice.Options),
                ExportRules.Srt => SrtWriter.Write(transcript),
                _ => TranscriptJson(await File.ReadAllTextAsync(Path.Combine(folder, ProjectLayout.TranscriptFile), cancellationToken), project, exportedAt),
            };
            items.Add(TextItem(ExportComponents.Transcript, ExportNaming.TranscriptFile(baseName, format), null, content));
        }

        return formats.Any(f => f is ExportRules.Markdown or ExportRules.Text) ? choice.Options ?? TranscriptTextOptions.Default : null;
    }
}
