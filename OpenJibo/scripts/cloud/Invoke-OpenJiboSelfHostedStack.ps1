param(
    [switch]$RunMigration,
    [switch]$SkipBuild,
    [string]$Image
)

$ErrorActionPreference = "Stop"

$imagePattern = '^(?:localhost(?::[0-9]+)?|[a-z0-9]+(?:[.-][a-z0-9]+)*(?::[0-9]+)?)(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)+@sha256:[0-9a-f]{64}$'
if ($PSBoundParameters.ContainsKey('Image')) {
    if ($Image -cnotmatch $imagePattern) {
        throw '-Image must be a digest-pinned registry/repository@sha256:<64 lowercase hex> reference.'
    }
    if ($SkipBuild) {
        throw '-Image already selects no-build mode; do not combine it with -SkipBuild.'
    }
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))

$previousImage = [Environment]::GetEnvironmentVariable('OPENJIBO_RUNTIME_IMAGE', 'Process')
if ($PSBoundParameters.ContainsKey('Image')) {
    $env:OPENJIBO_RUNTIME_IMAGE = $Image
}
else {
    $env:OPENJIBO_RUNTIME_IMAGE = 'openjibo-cloud:self-hosted'
}

try {
    & (Join-Path $PSScriptRoot "Initialize-OpenJiboComposeEnv.ps1") -RepoRoot $repoRoot

    $composeArgs = @("compose", "up", "-d")
    if ($PSBoundParameters.ContainsKey('Image')) {
        $composeArgs += @('--no-build', '--pull', 'missing')
    }
    elseif (-not $SkipBuild) {
        $composeArgs += "--build"
    }

    $composeArgs += "postgres"
    if ($RunMigration) {
        $composeArgs += "migrate"
    }
    $composeArgs += "api"

    Push-Location $repoRoot
    try {
        & docker compose config --quiet *> $null
        if ($LASTEXITCODE -ne 0) {
            throw 'Docker Compose configuration check failed; no services were started.'
        }
        & docker @composeArgs
        if ($LASTEXITCODE -ne 0) {
            throw "Docker Compose failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    [Environment]::SetEnvironmentVariable('OPENJIBO_RUNTIME_IMAGE', $previousImage, 'Process')
}
