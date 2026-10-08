<#
.SYNOPSIS
  Writes a synthetic two-voice recording with every line at a known time, for checking that transcript times
  match the audio (tools/e2e/m6-sync.mjs, ENGINE-NOTES.md §K). Lines are spoken by Windows' own speech synthesizer
  (System.Speech) straight at the chosen rate, placed after silences of known, varied length (one of 20 s), and the
  file is padded to at least -Minutes. Beside the WAV a JSON file lists where each line's speech really starts
  and ends (the first and last 10 ms frame above -40 dBFS in its clip, plus where the clip was placed). The lines
  are made up; nothing is downloaded.

.PARAMETER Path
  Where to write the WAV (keep it under artifacts/, which git ignores). The truth is written to <Path>.json.

.PARAMETER SampleRate
  44100 (default, like most MP3s) or 48000, 22050, 16000.

.PARAMETER Channels
  1 (default) or 2 (the same speech in both channels).

.EXAMPLE
  powershell -File tools/e2e/sync-fixture.ps1 -Path artifacts/e2e-fixtures/sync-44k.wav -SampleRate 44100
#>
param(
  [Parameter(Mandatory = $true)] [string] $Path,
  [int] $SampleRate = 44100,
  [int] $Channels = 1,
  [double] $Minutes = 4
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
Add-Type -TypeDefinition @'
using System;
public static class SyncFixture
{
    // First and last 10 ms frame whose RMS is above -40 dBFS, in samples; -1 when the clip is silent.
    public static int[] Speech(byte[] pcm16, int rate)
    {
        int frame = rate / 100, n = pcm16.Length / 2, first = -1, last = -1;
        for (int f = 0; f + frame <= n; f += frame)
        {
            double sum = 0;
            for (int i = f; i < f + frame; i++) { double s = BitConverter.ToInt16(pcm16, 2 * i) / 32768.0; sum += s * s; }
            if (Math.Sqrt(sum / frame) > 0.01) { if (first < 0) first = f; last = f + frame; }
        }
        return new[] { first, last };
    }

    // Copies mono 16-bit samples into an interleaved buffer at a frame position, on every channel.
    public static void Place(byte[] pcm16, byte[] target, long frame, int channels)
    {
        int n = pcm16.Length / 2;
        for (int i = 0; i < n; i++)
            for (int c = 0; c < channels; c++)
            {
                long at = 2 * ((frame + i) * channels + c);
                target[at] = pcm16[2 * i];
                target[at + 1] = pcm16[2 * i + 1];
            }
    }
}
'@

$lines = @(
  'Good morning everyone, thanks for joining the planning call.',
  'Morning. I have the numbers from last week ready to share.',
  'Great. Let us begin with the delivery schedule for the spring release.',
  'The build is on track, but the installer still needs one more review.',
  'Who is reviewing the installer this time?',
  'I can take it. I will finish the review by Wednesday afternoon.',
  'Thank you. Next item is the customer survey that went out on Monday.',
  'We have about two hundred answers so far, which is more than expected.',
  'What is the most common request in the answers?',
  'Mostly people ask for a dark theme and for faster search in long lists.',
  'Both are already planned, so that is good news for the roadmap.',
  'Should we tell the people who answered when the changes will arrive?',
  'Yes, a short note in the next newsletter would be enough.',
  'Then let us move on to the budget for the second quarter.',
  'The budget is unchanged, apart from a small increase for testing devices.',
  'How many new devices do we need for the test lab?',
  'Three laptops and two tablets should cover the screens we support.',
  'That sounds reasonable. Please send the order before the end of the month.',
  'I will send it tomorrow morning and copy you on the email.',
  'Before we finish, can someone summarise the open questions?',
  'There are two. The release date for the tablet version, and the support hours.',
  'The tablet date depends on the review of the touch controls next week.',
  'And the support hours depend on how many people we can hire in March.',
  'Let us keep both on the list and look at them again in two weeks.',
  'Agreed. I will add them to the notes and send the notes out today.',
  'One more thing. The printer on the second floor is out of paper again.',
  'I will order more paper together with the testing devices.',
  'Is there anything else we should cover before we finish?',
  'Only a reminder that the office is closed on Friday for maintenance.',
  'Good to know. Thank you all, and see you next week.'
)
# Silence before each line, in seconds: varied, one long pause, never a fixed rhythm.
$gaps = @(1.5, 2.5, 4.0, 3.0, 6.0, 2.0, 5.0, 3.5, 2.5, 20.0, 3.0, 4.5, 2.0, 7.0, 3.0, 2.5, 5.5, 3.0, 4.0, 2.0, 6.5, 3.0, 4.0, 2.5, 5.0, 3.5, 8.0, 2.0, 4.5, 3.0)
if ($gaps.Count -ne $lines.Count) { throw 'Every line needs a gap.' }

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voices = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo.Name })
if ($voices.Count -eq 0) { throw 'No Windows speech voices are installed.' }
$first = $voices[0]
$second = if ($voices.Count -gt 1) { $voices[1] } else { $voices[0] }
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo($SampleRate, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$synth.Rate = 1

$clips = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
  $synth.SelectVoice($(if ($i % 2 -eq 0) { $first } else { $second }))
  $stream = New-Object System.IO.MemoryStream
  $synth.SetOutputToAudioStream($stream, $format)
  $synth.Speak($lines[$i])
  $synth.SetOutputToNull()
  $clips += , $stream.ToArray()
  $stream.Dispose()
}
$synth.Dispose()

# Place the clips: each line's speech starts $gaps[i] seconds after the previous line's speech ended.
$placed = @()
$cursor = 0.0
for ($i = 0; $i -lt $clips.Count; $i++) {
  $speech = [SyncFixture]::Speech($clips[$i], $SampleRate)
  if ($speech[0] -lt 0) { throw "Line $i came out silent." }
  $start = $cursor + $gaps[$i]
  $frame = [long][Math]::Round($start * $SampleRate) - $speech[0]
  $placed += [pscustomobject]@{ Index = $i; Frame = $frame; First = $speech[0]; Last = $speech[1] }
  $cursor = ($frame + $speech[1]) / $SampleRate
}
$totalFrames = [long][Math]::Max([Math]::Ceiling(($cursor + 3) * $SampleRate), [Math]::Ceiling($Minutes * 60 * $SampleRate))
$data = New-Object byte[] ($totalFrames * 2 * $Channels)
for ($i = 0; $i -lt $clips.Count; $i++) {
  [SyncFixture]::Place($clips[$i], $data, $placed[$i].Frame, $Channels)
}

$full = [System.IO.Path]::GetFullPath($Path)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $full) | Out-Null
$out = [System.IO.File]::Create($full)
$writer = New-Object System.IO.BinaryWriter($out)
$writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([uint32](36 + $data.Length))
$writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt ')); $writer.Write([uint32]16)
$writer.Write([uint16]1); $writer.Write([uint16]$Channels); $writer.Write([uint32]$SampleRate)
$writer.Write([uint32]($SampleRate * 2 * $Channels)); $writer.Write([uint16](2 * $Channels)); $writer.Write([uint16]16)
$writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([uint32]$data.Length); $writer.Write($data)
$writer.Dispose()

$truth = [ordered]@{
  schemaVersion = 1
  sampleRate = $SampleRate
  channels = $Channels
  durationSeconds = [Math]::Round($totalFrames / $SampleRate, 3)
  lines = @($placed | ForEach-Object {
      [ordered]@{
        index = $_.Index
        text = $lines[$_.Index]
        start = [Math]::Round(($_.Frame + $_.First) / $SampleRate, 3)
        end = [Math]::Round(($_.Frame + $_.Last) / $SampleRate, 3)
      }
    })
}
$truth | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 -Path ($full + '.json')
$lastStart = ($placed[-1].Frame + $placed[-1].First) / $SampleRate
Write-Output ("Wrote {0} ({1:0.0} s, {2} Hz, {3} ch, {4} lines, the last at {5:0.0} s) and its truth {0}.json." -f $Path, ($totalFrames / $SampleRate), $SampleRate, $Channels, $lines.Count, $lastStart)
