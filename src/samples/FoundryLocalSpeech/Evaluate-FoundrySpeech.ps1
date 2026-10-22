[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AudioFile,
    [Parameter(Mandatory)][string]$ModelId,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [ValidateSet('en', 'es', 'auto')][string]$Language = 'es',
    [double]$StartSeconds = 60,
    [double]$DurationSeconds = 25,
    [string]$Ffmpeg = 'ffmpeg',
    [switch]$Streaming,
    [switch]$PcmStreaming
)

$ErrorActionPreference = 'Stop'
if ($Streaming -and $PcmStreaming) { throw 'Select -Streaming (file input) or -PcmStreaming (incremental PCM input), not both.' }
if ([double]::IsNaN($StartSeconds) -or [double]::IsInfinity($StartSeconds) -or $StartSeconds -lt 0 -or
    [double]::IsNaN($DurationSeconds) -or [double]::IsInfinity($DurationSeconds) -or $DurationSeconds -le 0) {
    throw 'Start must be nonnegative and duration must be positive and finite.'
}
$inputPath = (Resolve-Path -LiteralPath $AudioFile).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory to preserve existing evidence.' }
$decoder = (Get-Command $Ffmpeg -ErrorAction Stop).Source
New-Item -ItemType Directory -Path $output | Out-Null
$wav = Join-Path $output 'input.wav'
$culture = [System.Globalization.CultureInfo]::InvariantCulture
$start = $StartSeconds.ToString('R', $culture)
$duration = $DurationSeconds.ToString('R', $culture)
$decodeArgs = @('-hide_banner', '-loglevel', 'error', '-nostdin', '-i', $inputPath,
    '-ss', $start, '-t', $duration, '-vn', '-ac', '1', '-ar', '16000', '-c:a', 'pcm_s16le', $wav)
& $decoder @decodeArgs
if ($LASTEXITCODE -ne 0) { throw "FFmpeg failed with exit code $LASTEXITCODE." }
$version = & $decoder -version
if ($LASTEXITCODE -ne 0) { throw 'Could not read FFmpeg version.' }
$manifest = [ordered]@{
    sourceFile = [System.IO.Path]::GetFileName($inputPath)
    sourceSha256 = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
    decodedSha256 = (Get-FileHash -LiteralPath $wav -Algorithm SHA256).Hash
    sourceOffsetSeconds = $StartSeconds
    requestedDurationSeconds = $DurationSeconds
    ffmpegVersion = $version[0]
    decodeArguments = $decodeArgs
    modelId = $ModelId
    language = $Language
    streaming = [bool]$Streaming
    pcmStreaming = [bool]$PcmStreaming
    inputMode = if ($PcmStreaming) { 'incremental-pcm' } else { 'file' }
    pcmChunkBytes = if ($PcmStreaming) { 4096 } else { $null }
    createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    complete = $false
}
$manifestPath = Join-Path $output 'preparation.json'
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$mode = if ($PcmStreaming) { 'pcm-stream' } elseif ($Streaming) { 'stream' } else { 'transcribe' }
& dotnet run --project $PSScriptRoot -c Release -- $mode $ModelId $wav (Join-Path $output 'run') $Language $start
$code = $LASTEXITCODE
$manifest['backendExitCode'] = $code
$manifest['complete'] = $code -eq 0 -or $code -eq 2
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
if ($code -ne 0 -and $code -ne 2) { throw "Foundry evaluation failed with exit code $code. Inspect saved failure evidence." }
if ($code -eq 2) { Write-Warning 'Inference completed, but the subtitle timing gate is blocked. No subtitles were fabricated.' }
exit $code
