param(
    [switch]$FullEpisodes,
    [switch]$ValidateOnly,
    [string]$OutputDirectory = 'artifacts\multilingual'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Push-Location $repo
try {
    if (-not $ValidateOnly) {
        dotnet build "$PSScriptRoot\PodcastToSrt.csproj" -c Release --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Sample build failed.' }
    }
    $voice = Join-Path $PSScriptRoot 'assets\ntn459\Recording.m4a'
    $edited = Join-Path $PSScriptRoot 'assets\ntn459\ntn459.mp3'
    $cases = @(
        @{ Name='voice-base-es'; Input=$voice; Start=60; Duration=50; Model='whisper-base'; Language='es'; Cleanup='off' },
        @{ Name='voice-small-es'; Input=$voice; Start=60; Duration=50; Model='whisper-small'; Language='es'; Cleanup='off' },
        @{ Name='edited-base-es'; Input=$edited; Start=0; Duration=90; Model='whisper-base'; Language='es'; Cleanup='off' },
        @{ Name='edited-small-es'; Input=$edited; Start=0; Duration=90; Model='whisper-small'; Language='es'; Cleanup='off' },
        @{ Name='voice-base-auto'; Input=$voice; Start=60; Duration=25; Model='whisper-base'; Language='auto'; Cleanup='auto' },
        @{ Name='voice-base-english'; Input=$voice; Start=60; Duration=25; Model='whisper-base'; Language='es'; Cleanup='auto'; Translate=$true },
        @{ Name='voice-base-experimental'; Input=$voice; Start=60; Duration=25; Model='whisper-base'; Language='es'; Cleanup='experimental' },
        @{ Name='voice-base-late'; Input=$voice; Start=1320; Duration=50; Model='whisper-base'; Language='es'; Cleanup='off' }
    )
    if ($FullEpisodes) {
        $cases += @(
            @{ Name='voice-full-base-es'; Input=$voice; Model='whisper-base'; Language='es'; Cleanup='off' },
            @{ Name='edited-full-base-es'; Input=$edited; Model='whisper-base'; Language='es'; Cleanup='off'; Vtt=$true }
        )
    }
    foreach ($case in $cases) {
        $extension = if ($case.Vtt) { '.vtt' } else { '.srt' }
        $output = Join-Path $OutputDirectory ($case.Name + $extension)
        $arguments = @('run', '--project', "$PSScriptRoot\PodcastToSrt.csproj", '-c', 'Release', '--no-build', '--',
            $case.Input, $output, '--model', $case.Model, '--language', $case.Language, '--cleanup', $case.Cleanup)
        if ($case.ContainsKey('Start')) { $arguments += @('--start-seconds', $case.Start, '--duration-seconds', $case.Duration) }
        if ($case.Translate) { $arguments += '--translate' }
        if ($case.Vtt) { $arguments += '--vtt' }
        Write-Host "`n=== $($case.Name) ==="
        if (-not $ValidateOnly) {
            dotnet @arguments
            if ($LASTEXITCODE -ne 0) { throw "Evaluation failed: $($case.Name)" }
        }
        $report = Get-Content ([IO.Path]::ChangeExtension($output, '.json')) -Raw | ConvertFrom-Json
        if (-not $report.complete -or [Math]::Abs($report.audioSeconds - $report.processedSeconds) -gt 0.001) {
            throw "Incomplete audio processing: $($case.Name)"
        }
        $expectedOffset = $report.sourceOffsetSeconds
        foreach ($window in $report.windows) {
            if ([Math]::Abs($window.offsetSeconds - $expectedOffset) -gt 0.001 -or
                $window.durationSeconds -le 0 -or $window.durationSeconds -gt 25.001) {
                throw "Invalid window bounds or gap: $($case.Name)"
            }
            $expectedOffset += $window.durationSeconds
        }
        $previousEnd = $report.sourceOffsetSeconds
        $timings = [regex]::Matches((Get-Content $output -Raw),
            '(?m)^(\d{2}:\d{2}:\d{2}[,.]\d{3}) --> (\d{2}:\d{2}:\d{2}[,.]\d{3})')
        $expectedCues = ($report.windows | Measure-Object segmentCount -Sum).Sum
        if ($timings.Count -ne $expectedCues) {
            throw "Missing or malformed subtitle cues: $($case.Name)"
        }
        foreach ($timing in $timings) {
            $start = [TimeSpan]::Parse($timing.Groups[1].Value.Replace(',', '.'),
                [Globalization.CultureInfo]::InvariantCulture).TotalSeconds
            $end = [TimeSpan]::Parse($timing.Groups[2].Value.Replace(',', '.'),
                [Globalization.CultureInfo]::InvariantCulture).TotalSeconds
            if ($start -lt $previousEnd - 0.001 -or $end -le $start -or $end -gt $expectedOffset + 0.001) {
                throw "Invalid or overlapping subtitle bounds: $($case.Name)"
            }
            $previousEnd = $end
        }
    }
}
finally {
    Pop-Location
}
