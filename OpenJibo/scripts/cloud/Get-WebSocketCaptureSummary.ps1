param(
    [string]$CaptureDirectory = "..\..\captures\websocket"
)

$resolvedDirectory = Resolve-Path -LiteralPath $CaptureDirectory -ErrorAction Stop
$eventFiles = Get-ChildItem -LiteralPath $resolvedDirectory -Filter *.events.ndjson -File | Sort-Object LastWriteTimeUtc

if (-not $eventFiles) {
    Write-Host "No websocket telemetry event files found in $resolvedDirectory"
    exit 0
}

$records = foreach ($file in $eventFiles) {
    Get-Content -LiteralPath $file.FullName | Where-Object { $_.Trim().Length -gt 0 } | ForEach-Object {
        $_ | ConvertFrom-Json
    }
}

$records |
    Group-Object EventType |
    Sort-Object Name |
    Select-Object Name, Count |
    Format-Table -AutoSize

$fixtureDirectory = Join-Path $resolvedDirectory "fixtures"
if (Test-Path -LiteralPath $fixtureDirectory) {
    Write-Host ""
    Write-Host "Exported websocket fixtures:"
    $fixtureFiles = Get-ChildItem -LiteralPath $fixtureDirectory -Filter *.flow.json -File |
        Sort-Object LastWriteTimeUtc

    $fixtureFiles |
        Select-Object LastWriteTimeUtc, Name |
        Format-Table -AutoSize

    $sttReplayFixtures = $fixtureFiles | Where-Object {
        $_.Name -like '*buffered-audio*' -or $_.Name -like '*short-burst*'
    }

    if ($sttReplayFixtures) {
        Write-Host ""
        Write-Host "STT replay fixtures to check first:"
        $sttReplayFixtures | Select-Object Name | Format-Table -AutoSize
    }
}
