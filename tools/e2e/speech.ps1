<#
.SYNOPSIS
  Writes a short synthetic two-voice conversation as a 16 kHz mono WAV with Windows' own speech
  synthesizer (System.Speech), for end-to-end runs that need a transcript with two speakers and no
  real recording. The lines are made up; nothing is downloaded.

.PARAMETER Path
  Where to write the WAV (keep it under artifacts/, which git ignores).

.EXAMPLE
  powershell -File tools/e2e/speech.ps1 -Path artifacts/e2e-fixtures/two-voices.wav
#>
param([Parameter(Mandatory = $true)] [string] $Path)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech

$lines = @(
  'Good morning. Let us start with the library screen and the row height.',
  'Thanks. I think the rows should stay at sixty eight pixels, they read well.',
  'Agreed. Next, the dark theme. Do we ship it in this release or the next one?',
  'In this release. The colours are done and the contrast checks have passed.',
  'Good. Then the last item is the empty state for a new library.',
  'I will write the copy for the empty state by Friday and share it with everyone.',
  'Perfect. That is everything for today. Thank you both.'
)

$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$voices = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled } | ForEach-Object { $_.VoiceInfo.Name })
if ($voices.Count -eq 0) { throw 'No Windows speech voices are installed.' }
$first = $voices[0]
$second = if ($voices.Count -gt 1) { $voices[1] } else { $voices[0] }

$folder = Split-Path -Parent ([System.IO.Path]::GetFullPath($Path))
New-Item -ItemType Directory -Force -Path $folder | Out-Null
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$synth.SetOutputToWaveFile([System.IO.Path]::GetFullPath($Path), $format)
for ($i = 0; $i -lt $lines.Count; $i++) {
  $synth.SelectVoice($(if ($i % 2 -eq 0) { $first } else { $second }))
  $prompt = New-Object System.Speech.Synthesis.PromptBuilder
  $prompt.AppendText($lines[$i])
  $prompt.AppendBreak([TimeSpan]::FromMilliseconds(900))
  $synth.Speak($prompt)
}
$synth.SetOutputToNull()
$synth.Dispose()
Write-Output "Wrote $Path with voices '$first' and '$second'."
