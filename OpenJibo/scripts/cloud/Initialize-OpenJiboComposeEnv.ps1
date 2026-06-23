param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
)

$composeDir = $RepoRoot
$templatePath = Join-Path $composeDir ".env.example"
$targetPath = Join-Path $composeDir ".env"

if (-not (Test-Path -LiteralPath $templatePath)) {
    throw "Missing compose env template: $templatePath"
}

if (Test-Path -LiteralPath $targetPath) {
    Write-Host "Compose env already exists: $targetPath"
    return
}

Copy-Item -LiteralPath $templatePath -Destination $targetPath
Write-Host "Created compose env from template: $targetPath"
