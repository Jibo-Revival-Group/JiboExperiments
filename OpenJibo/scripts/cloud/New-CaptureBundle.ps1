param(
    [string]$CaptureRoot = "..\..\captures",
    [string]$BundleDirectory = "..\..\captures\bundles",
    [string]$BundleName
)

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    $normalizedBase = [System.IO.Path]::GetFullPath($BasePath)
    if (-not $normalizedBase.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $normalizedBase = $normalizedBase + [System.IO.Path]::DirectorySeparatorChar
    }

    $normalizedFull = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $normalizedFull.StartsWith($normalizedBase, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path '$FullPath' is not under '$BasePath'."
    }

    return $normalizedFull.Substring($normalizedBase.Length)
}

$resolvedCaptureRoot = Resolve-Path -LiteralPath $CaptureRoot -ErrorAction Stop
$resolvedBundleDirectory = Resolve-Path -LiteralPath $BundleDirectory -ErrorAction SilentlyContinue
if (-not $resolvedBundleDirectory) {
    $resolvedBundleDirectory = New-Item -ItemType Directory -Force -Path $BundleDirectory | Select-Object -ExpandProperty FullName
}
else {
    $resolvedBundleDirectory = $resolvedBundleDirectory.Path
}

if ([string]::IsNullOrWhiteSpace($BundleName)) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $BundleName = "capture-bundle-$timestamp"
}

$stagingDirectory = Join-Path $resolvedBundleDirectory "$BundleName.staging"
$archivePath = Join-Path $resolvedBundleDirectory "$BundleName.zip"

if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null

try {
    $sourceFiles = Get-ChildItem -LiteralPath $resolvedCaptureRoot -Recurse -File | Where-Object {
        $_.Name -eq "capture-index.ndjson" -or
        $_.Name -like "*.events.ndjson" -or
        $_.Name -like "*.flow.json"
    }

    if (-not $sourceFiles) {
        Write-Host "No capture files were found under $resolvedCaptureRoot"
        exit 0
    }

    foreach ($file in $sourceFiles) {
        $relativePath = Get-RelativePath -BasePath $resolvedCaptureRoot -FullPath $file.FullName
        $destinationPath = Join-Path $stagingDirectory $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath

        if (-not (Test-Path -LiteralPath $destinationDirectory)) {
            New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        }

        Copy-Item -LiteralPath $file.FullName -Destination $destinationPath -Force
    }

    $captureIndexFiles = @($sourceFiles | Where-Object { $_.Name -eq "capture-index.ndjson" })
    $eventFiles = @($sourceFiles | Where-Object { $_.Name -like "*.events.ndjson" })
    $fixtureFiles = @($sourceFiles | Where-Object { $_.Name -like "*.flow.json" })
    $fixtureNames = @($fixtureFiles | Sort-Object FullName | ForEach-Object {
        Get-RelativePath -BasePath $resolvedCaptureRoot -FullPath $_.FullName
    })

    $manifest = [ordered]@{
        createdUtc = (Get-Date).ToUniversalTime().ToString("O")
        sourceRoot = $resolvedCaptureRoot
        fileCount = $sourceFiles.Count
        captureIndexCount = $captureIndexFiles.Count
        eventFileCount = $eventFiles.Count
        fixtureCount = $fixtureFiles.Count
        fixtureNames = $fixtureNames
    }

    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $stagingDirectory "bundle-manifest.json") -Encoding utf8

    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $archivePath -Force
    Write-Host "Created capture bundle at $archivePath"
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
