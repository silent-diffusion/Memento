using System.Globalization;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Voices;

/// <summary>
/// The <c>voices.*</c> bridge methods (2.0, DESIGN.md §19): known voices learned from names the user confirms in Review
/// (opt-in, Settings › Speakers › "Remember speakers by voice"), suggested on later recordings, never applied by
/// themselves. A confirmation is keyed by recording and speaker, so naming the same speaker again moves its sample to
/// the new name; a voice left without samples is forgotten. Each enrolment answers a change id that
/// <c>voices.revert</c> takes back (Review's Undo), from a journal kept in memory for this run of Memento; forgetting a
/// voice drops it from the journal, so an Undo never brings a forgotten voice back.
/// </summary>
public sealed partial class KnownVoiceService(
    KnownVoicesStore store,
    TranscriptStore transcripts,
    IProjectStore projects,
    TranscriptService transcriptService,
    ISettingsStore settings,
    TimeProvider time,
    ILogger<KnownVoiceService> logger)
{
    /// <summary>At most this many enrolments can be reverted.</summary>
    public const int JournalSize = 200;

    private readonly object _journalLock = new();
    private readonly LinkedList<Change> _journal = new();
    private readonly ILogger<KnownVoiceService> _logger = logger;

    private bool Remember => settings.Current.Speakers.RememberVoices;

    public async Task<KnownVoicesResult> ListAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken);
        return Result(document);
    }

    public async Task<KnownVoicesResult> SetSuggestAsync(string voiceId, bool suggest, CancellationToken cancellationToken)
    {
        var document = await UpdateAsync(
            d =>
            {
                var voice = Find(d, voiceId);
                return voice.Suggest == suggest ? (null, d) : Replace(d, voice with { Suggest = suggest });
            },
            cancellationToken);
        return Result(document);
    }

    public async Task<KnownVoicesResult> ForgetAsync(string voiceId, CancellationToken cancellationToken)
    {
        var document = await UpdateAsync(
            d =>
            {
                var voice = Find(d, voiceId);
                var next = d with { Voices = d.Voices.Where(v => v.Id != voice.Id).ToList() };
                return (next, next);
            },
            cancellationToken);
        Purge(voiceId);
        LogForgot(voiceId);
        return Result(document);
    }

    public async Task<KnownVoicesResult> ForgetAllAsync(CancellationToken cancellationToken)
    {
        await store.DeleteAsync(cancellationToken);
        Purge(null);
        return new KnownVoicesResult(Remember, []);
    }

    /// <summary>
    /// <c>voices.remember</c>: learns the named speaker's voice under its name (or refines the voice that has the name),
    /// when the setting is on, voices were kept for the recording and the speaker has enough voiced speech.
    /// </summary>
    public async Task<VoiceRememberResult> RememberAsync(string recordingId, string speakerId, CancellationToken cancellationToken)
    {
        var transcript = await TranscriptAsync(recordingId, cancellationToken);
        var speaker = transcript.Speakers.FirstOrDefault(s => s.Id == speakerId)
            ?? throw new BridgeException(
                DomainErrorCodes.TranscriptSpeakerNotFound,
                "That speaker is not in this transcript any more; speakers may have been merged or identified again. Nothing was remembered.",
                speakerId);
        if (!Remember)
        {
            return new VoiceRememberResult(false, null, null, "\"Remember speakers by voice\" is off in Settings › Speakers, so no voice was learned.");
        }

        if (!speaker.Renamed)
        {
            return new VoiceRememberResult(false, null, null, $"{speaker.Name} has no name yet, so there is nothing to remember the voice under.");
        }

        var voices = await transcripts.LoadVoicesAsync(recordingId, cancellationToken);
        if (voices is null)
        {
            return new VoiceRememberResult(false, null, null, $"The voices of this recording were not kept when its speakers were identified, so {speaker.Name}'s voice was not learned. Identify speakers again to keep them.");
        }

        var print = SpeakerVoices.Of(speaker.Id, transcript.Segments, SpeakerVoices.OfLines(voices));
        if (print is null || print.Seconds < VoiceMatcher.MinSeconds)
        {
            return new VoiceRememberResult(
                false,
                null,
                null,
                string.Create(CultureInfo.InvariantCulture, $"{speaker.Name} speaks for {Math.Round(print?.Seconds ?? 0):0} s with a usable voice here; Memento learns a voice from {VoiceMatcher.MinSeconds:0} s or more, so it was not remembered."));
        }

        var sample = new KnownVoiceSample(recordingId, speaker.Id, print.Direction.Select(x => (float)x).ToArray(), Math.Round(print.Seconds, 1), time.GetLocalNow());
        var (changeId, voice) = await EnrolAsync(sample, speaker.Name, voices.EmbeddingModelId, cancellationToken);
        LogRemembered(voice.Id, recordingId);
        return new VoiceRememberResult(true, changeId, Info(voice), null);
    }

    /// <summary><c>voices.revert</c>: takes an enrolment back (Undo). A voice forgotten since stays forgotten.</summary>
    public async Task RevertAsync(string changeId, CancellationToken cancellationToken)
    {
        Change? change;
        lock (_journalLock)
        {
            change = _journal.FirstOrDefault(c => c.Id == changeId);
        }

        if (change is null)
        {
            throw new BridgeException(
                DomainErrorCodes.VoicesNotFound,
                "That voice change cannot be taken back any more: Memento was restarted since, or the voice was forgotten. Nothing was changed.",
                changeId);
        }

        await UpdateAsync(
            d =>
            {
                var list = d.Voices.ToList();
                foreach (var (voiceId, before) in change.Before)
                {
                    var index = list.FindIndex(v => v.Id == voiceId);
                    if (before is null)
                    {
                        if (index >= 0)
                        {
                            list.RemoveAt(index);
                        }
                    }
                    else if (index >= 0)
                    {
                        // The Settings switch and the declines were separate steps: they stay as they are now.
                        list[index] = before with { Suggest = list[index].Suggest, DeclinedIn = list[index].DeclinedIn };
                    }
                    else
                    {
                        list.Add(before);
                    }
                }

                var next = d with { Voices = list };
                return (next, next);
            },
            cancellationToken);
        lock (_journalLock)
        {
            _journal.Remove(change);
        }
    }

    /// <summary><c>voices.matches</c>: the known voices the recording's unnamed speakers sound like (none while the setting is off).</summary>
    public async Task<IReadOnlyList<VoiceMatch>> MatchesAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await ManifestAsync(recordingId, cancellationToken);
        if (!Remember)
        {
            return [];
        }

        var transcript = await transcripts.LoadAsync(recordingId, cancellationToken);
        var voices = transcript is null ? null : await transcripts.LoadVoicesAsync(recordingId, cancellationToken);
        if (transcript is null || voices is null)
        {
            return [];
        }

        var known = await LoadAsync(cancellationToken);
        var preferred = (manifest.Details.WhoSpoke?.Names ?? []).Concat(manifest.Details.Participants).ToList();
        return VoiceMatcher.Match(transcript.Segments, transcript.Speakers, voices, known.Voices, recordingId, preferred)
            .Select(m => new VoiceMatch(m.SpeakerId, m.Voice.Id, m.Voice.Name, Math.Round(m.Similarity, 3), m.Voice.Recordings.Count))
            .ToList();
    }

    /// <summary><c>voices.decline</c>: "Not {name}" hides the voice's suggestion in this recording; <c>false</c> shows it again.</summary>
    public async Task<IReadOnlyList<VoiceMatch>> DeclineAsync(string recordingId, string voiceId, bool declined, CancellationToken cancellationToken)
    {
        await ManifestAsync(recordingId, cancellationToken);
        await UpdateAsync(
            d =>
            {
                var voice = Find(d, voiceId);
                var has = voice.DeclinedIn.Contains(recordingId, StringComparer.Ordinal);
                if (has == declined)
                {
                    return (null, d);
                }

                var list = declined
                    ? voice.DeclinedIn.Append(recordingId).TakeLast(KnownVoice.MaxRecordings).ToList()
                    : voice.DeclinedIn.Where(r => r != recordingId).ToList();
                return Replace(d, voice with { DeclinedIn = list });
            },
            cancellationToken);
        return await MatchesAsync(recordingId, cancellationToken);
    }

    /// <summary>
    /// <c>voices.acceptMatch</c> ("Use name"): renames the speaker to the known voice's name (<c>transcript.renameSpeaker</c>)
    /// and refines the voice with this recording.
    /// </summary>
    public async Task<VoiceAcceptResult> AcceptAsync(string recordingId, string speakerId, string voiceId, CancellationToken cancellationToken)
    {
        var known = await LoadAsync(cancellationToken);
        var voice = Find(known, voiceId);
        var speakers = await transcriptService.RenameSpeakerAsync(recordingId, speakerId, voice.Name, cancellationToken);
        var remembered = await RememberAsync(recordingId, speakerId, cancellationToken);
        return new VoiceAcceptResult(speakers, remembered.ChangeId);
    }

    /// <summary>The Delete flow: a deleted recording's confirmations go too (a voice left without any is forgotten).</summary>
    public async Task ForgetRecordingAsync(string recordingId, CancellationToken cancellationToken)
    {
        if (!File.Exists(store.FilePath))
        {
            return;
        }

        var emptied = new List<string>();
        await UpdateAsync(
            d =>
            {
                if (d.Voices.All(v => v.Samples.All(s => s.RecordingId != recordingId) && !v.Recordings.Contains(recordingId) && !v.DeclinedIn.Contains(recordingId)))
                {
                    return (null, d);
                }

                var list = new List<KnownVoice>();
                foreach (var voice in d.Voices)
                {
                    var samples = voice.Samples.Where(s => s.RecordingId != recordingId).ToList();
                    if (samples.Count == 0 && voice.Samples.Count > 0)
                    {
                        emptied.Add(voice.Id);
                        continue;
                    }

                    list.Add(voice with
                    {
                        Samples = samples,
                        Recordings = voice.Recordings.Where(r => r != recordingId).ToList(),
                        DeclinedIn = voice.DeclinedIn.Where(r => r != recordingId).ToList(),
                    });
                }

                var next = d with { Voices = list };
                return (next, next);
            },
            cancellationToken);
        foreach (var id in emptied)
        {
            Purge(id);
        }
    }

    private async Task<(string ChangeId, KnownVoice Voice)> EnrolAsync(KnownVoiceSample sample, string name, string embeddingModelId, CancellationToken cancellationToken)
    {
        var before = new List<(string, KnownVoice?)>();
        KnownVoice? enrolled = null;
        await UpdateAsync(
            d =>
            {
                var list = new List<KnownVoice>();

                // The speaker's earlier confirmation (under this or another name) is replaced by this one.
                foreach (var voice in d.Voices)
                {
                    var samples = voice.Samples.Where(s => !(s.RecordingId == sample.RecordingId && s.SpeakerId == sample.SpeakerId)).ToList();
                    if (samples.Count == voice.Samples.Count)
                    {
                        list.Add(voice);
                        continue;
                    }

                    before.Add((voice.Id, voice));
                    if (samples.Count > 0 || VoiceMatcher.Key(voice.Name) == VoiceMatcher.Key(name))
                    {
                        var stillHere = samples.Any(s => s.RecordingId == sample.RecordingId);
                        list.Add(voice with { Samples = samples, Recordings = stillHere ? voice.Recordings : voice.Recordings.Where(r => r != sample.RecordingId).ToList() });
                    }
                }

                var index = list.FindIndex(v => VoiceMatcher.Key(v.Name) == VoiceMatcher.Key(name) && v.EmbeddingModelId == embeddingModelId);
                if (index < 0)
                {
                    enrolled = new KnownVoice
                    {
                        Id = KnownVoicesMigration.NewId(),
                        Name = name,
                        EmbeddingModelId = embeddingModelId,
                        Samples = [sample],
                        Recordings = [sample.RecordingId],
                        CreatedAt = sample.At,
                        LastConfirmedAt = sample.At,
                    };
                    before.Add((enrolled.Id, null));
                    list.Add(enrolled);
                }
                else
                {
                    var voice = list[index];
                    if (before.All(b => b.Item1 != voice.Id))
                    {
                        before.Add((voice.Id, voice));
                    }

                    enrolled = voice with
                    {
                        Name = name,
                        Samples = voice.Samples.Append(sample).TakeLast(KnownVoice.MaxSamples).ToList(),
                        Recordings = voice.Recordings.Contains(sample.RecordingId) ? voice.Recordings : voice.Recordings.Append(sample.RecordingId).TakeLast(KnownVoice.MaxRecordings).ToList(),
                        DeclinedIn = voice.DeclinedIn.Where(r => r != sample.RecordingId).ToList(),
                        LastConfirmedAt = sample.At,
                    };
                    list[index] = enrolled;
                }

                var next = d with { Voices = list.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).ToList() };
                return (next, next);
            },
            cancellationToken);
        var change = new Change(Guid.NewGuid().ToString("N"), before);
        lock (_journalLock)
        {
            _journal.AddLast(change);
            while (_journal.Count > JournalSize)
            {
                _journal.RemoveFirst();
            }
        }

        return (change.Id, enrolled!);
    }

    private void Purge(string? voiceId)
    {
        lock (_journalLock)
        {
            for (var node = _journal.First; node is not null;)
            {
                var next = node.Next;
                if (voiceId is null || node.Value.Before.Any(b => b.VoiceId == voiceId))
                {
                    _journal.Remove(node);
                }

                node = next;
            }
        }
    }

    private KnownVoicesResult Result(KnownVoicesDocument document) =>
        new(Remember, document.Voices.OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase).Select(Info).ToList());

    private static KnownVoiceInfo Info(KnownVoice voice) => new(voice.Id, voice.Name, voice.Recordings.Count, voice.LastConfirmedAt, voice.Suggest);

    private static (KnownVoicesDocument, KnownVoicesDocument) Replace(KnownVoicesDocument document, KnownVoice voice)
    {
        var next = document with { Voices = document.Voices.Select(v => v.Id == voice.Id ? voice : v).ToList() };
        return (next, next);
    }

    private static KnownVoice Find(KnownVoicesDocument document, string voiceId) =>
        document.Voices.FirstOrDefault(v => v.Id == voiceId)
        ?? throw new BridgeException(
            DomainErrorCodes.VoicesNotFound,
            "That voice is not known any more; it may have been forgotten in Settings › Speakers. Nothing was changed.",
            voiceId);

    private async Task<KnownVoicesDocument> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(cancellationToken);
        }
        catch (KnownVoicesNewerException ex)
        {
            throw Newer(ex);
        }
    }

    private async Task<KnownVoicesDocument> UpdateAsync(Func<KnownVoicesDocument, (KnownVoicesDocument? Next, KnownVoicesDocument Result)> update, CancellationToken cancellationToken)
    {
        try
        {
            return await store.UpdateAsync(update, cancellationToken);
        }
        catch (KnownVoicesNewerException ex)
        {
            throw Newer(ex);
        }
    }

    private static BridgeException Newer(KnownVoicesNewerException ex) =>
        new(
            DomainErrorCodes.VoicesNewerVersion,
            $"The known voices were saved by a newer version of Memento (format {ex.Version}), so this version leaves them as they are. Nothing was changed. Update Memento to use them, or Forget all in Settings › Speakers to start again.",
            KnownVoicesStore.FileName);

    private async Task<ProjectManifest> ManifestAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await projects.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }
    }

    private async Task<TranscriptDocument> TranscriptAsync(string recordingId, CancellationToken cancellationToken)
    {
        await ManifestAsync(recordingId, cancellationToken);
        return await transcripts.LoadAsync(recordingId, cancellationToken)
            ?? throw new BridgeException(
                DomainErrorCodes.TranscriptNone,
                "This recording has no transcript yet, so there are no speakers to remember. Nothing was changed.",
                recordingId);
    }

    /// <param name="Before">Each voice the change touched, as it was before (<c>null</c>: the change created it).</param>
    private sealed record Change(string Id, IReadOnlyList<(string VoiceId, KnownVoice? Before)> Before);

    [LoggerMessage(Level = LogLevel.Information, Message = "Known voice {VoiceId} learned or refined from recording {RecordingId}")]
    private partial void LogRemembered(string voiceId, string recordingId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Known voice {VoiceId} forgotten")]
    private partial void LogForgot(string voiceId);
}
