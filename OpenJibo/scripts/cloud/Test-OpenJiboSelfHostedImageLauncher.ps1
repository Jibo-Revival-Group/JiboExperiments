$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('openjibo-image-launcher-' + [guid]::NewGuid().ToString('N'))
$scriptDir = $PSScriptRoot
$fixtureScriptDir = Join-Path $fixture 'repo/scripts/cloud'
$previousImage = [Environment]::GetEnvironmentVariable('OPENJIBO_RUNTIME_IMAGE', 'Process')

try {
    New-Item -ItemType Directory -Path $fixtureScriptDir -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $scriptDir 'Invoke-OpenJiboSelfHostedStack.ps1') -Destination $fixtureScriptDir
    Set-Content -LiteralPath (Join-Path $fixture 'repo/.env') -Value 'OPENJIBO_RUNTIME_IMAGE=example.invalid/poison:latest'
    Set-Content -LiteralPath (Join-Path $fixtureScriptDir 'Initialize-OpenJiboComposeEnv.ps1') -Value 'Add-Content -LiteralPath $env:TEST_LOG -Value "initialized"'
    $env:TEST_LOG = Join-Path $fixture 'calls.log'
    $launcher = Join-Path $fixtureScriptDir 'Invoke-OpenJiboSelfHostedStack.ps1'
    $digest = 'registry.example/openjibo/cloud@sha256:' + ('a' * 64)

    function docker {
        Add-Content -LiteralPath $env:TEST_LOG -Value ('docker:{0}:{1}' -f $env:OPENJIBO_RUNTIME_IMAGE, ($args -join ' '))
        $global:LASTEXITCODE = if ($env:FAKE_DOCKER_FAIL) { 37 } else { 0 }
    }

    $env:OPENJIBO_RUNTIME_IMAGE = 'example.invalid/poison:latest'
    & $launcher -Image $digest -RunMigration
    if ((Get-Content -LiteralPath $env:TEST_LOG -Raw) -notmatch [regex]::Escape("docker:$digest`:compose up -d --no-build --pull missing postgres migrate api")) {
        throw 'Digest image launch did not use the same pinned image without building.'
    }
    if ($env:OPENJIBO_RUNTIME_IMAGE -ne 'example.invalid/poison:latest') {
        throw 'Image launch did not restore the caller environment.'
    }

    Clear-Content -LiteralPath $env:TEST_LOG
    $env:FAKE_DOCKER_FAIL = '1'
    $failed = $false
    try { & $launcher -Image $digest } catch { $failed = $true }
    $env:FAKE_DOCKER_FAIL = $null
    if (-not $failed -or $env:OPENJIBO_RUNTIME_IMAGE -ne 'example.invalid/poison:latest') {
        throw 'Failed Docker launch did not report failure and restore the caller environment.'
    }

    Clear-Content -LiteralPath $env:TEST_LOG
    & $launcher
    if ((Get-Content -LiteralPath $env:TEST_LOG -Raw) -notmatch 'docker:openjibo-cloud:self-hosted:compose up -d --build postgres api') {
        throw 'Source launch did not override the inherited image.'
    }

    Clear-Content -LiteralPath $env:TEST_LOG
    & $launcher -SkipBuild
    if ((Get-Content -LiteralPath $env:TEST_LOG -Raw) -notmatch 'docker:openjibo-cloud:self-hosted:compose up -d postgres api') {
        throw 'SkipBuild source launch did not override the inherited image.'
    }

    $invalidImages = @(
        '',
        'registry.example/openjibo/cloud:latest',
        ('https://registry.example/openjibo/cloud@sha256:' + ('a' * 64)),
        ('user:pass@registry.example/openjibo/cloud@sha256:' + ('a' * 64)),
        'registry.example/openjibo/cloud@sha256:deadbeef',
        ('registry.example/openjibo/cloud@sha256:' + ('A' * 64)),
        ('registry.example/openjibo/cloud@sha256:' + ('a' * 64) + ' extra')
    )
    foreach ($invalidImage in $invalidImages) {
        Clear-Content -LiteralPath $env:TEST_LOG
        $rejected = $false
        try { & $launcher -Image $invalidImage } catch { $rejected = $true }
        if (-not $rejected -or (Get-Item -LiteralPath $env:TEST_LOG).Length -ne 0) {
            throw "Invalid image was accepted or caused side effects: $invalidImage"
        }
    }

    Clear-Content -LiteralPath $env:TEST_LOG
    $rejected = $false
    try { & $launcher -Image $digest -SkipBuild } catch { $rejected = $true }
    if (-not $rejected -or (Get-Item -LiteralPath $env:TEST_LOG).Length -ne 0) {
        throw 'Combined Image and SkipBuild flags were accepted or caused side effects.'
    }
    Write-Host 'PowerShell image launcher checks passed.'
}
finally {
    [Environment]::SetEnvironmentVariable('OPENJIBO_RUNTIME_IMAGE', $previousImage, 'Process')
    [Environment]::SetEnvironmentVariable('TEST_LOG', $null, 'Process')
    [Environment]::SetEnvironmentVariable('FAKE_DOCKER_FAIL', $null, 'Process')
    if (Test-Path -LiteralPath $fixture) {
        $resolvedFixture = [System.IO.Path]::GetFullPath($fixture)
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Path $resolvedFixture -Leaf).StartsWith('openjibo-image-launcher-', [StringComparison]::Ordinal)) {
            throw 'Refusing to remove a test fixture outside the temp directory.'
        }
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
    }
}
