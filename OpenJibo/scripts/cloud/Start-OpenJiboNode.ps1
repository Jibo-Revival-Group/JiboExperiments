param(
    [string]$NodeDirectory = "src/Jibo.Cloud/node",
    [switch]$Install
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedNodeDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $NodeDirectory))
$serverPath = Join-Path $resolvedNodeDirectory "open-jibo-link.js"
$packagePath = Join-Path $resolvedNodeDirectory "package.json"
$certPath = Join-Path $resolvedNodeDirectory "cert.pem"
$keyPath = Join-Path $resolvedNodeDirectory "key.pem"

if (-not (Test-Path -LiteralPath $serverPath)) {
    throw "Could not find Node server at $serverPath"
}

if (-not (Test-Path -LiteralPath $packagePath)) {
    throw "Could not find package.json at $packagePath"
}

if ($Install -or -not (Test-Path -LiteralPath (Join-Path $resolvedNodeDirectory "node_modules"))) {
    Write-Host "Installing Node dependencies"
    npm install --prefix $resolvedNodeDirectory
}

if (-not (Test-Path -LiteralPath $certPath) -or -not (Test-Path -LiteralPath $keyPath)) {
    Write-Warning "cert.pem and key.pem are not present in $resolvedNodeDirectory."
    Write-Warning "The Node oracle expects those files because it binds HTTPS on port 443."
    Write-Warning "Use the same dev certificate material that your controlled Jibo routing already trusts."
}

Write-Host "Starting OpenJibo Node protocol oracle"
Write-Host " - directory: $resolvedNodeDirectory"
Write-Host " - server: $serverPath"
Write-Host " - port: 443"

Push-Location $resolvedNodeDirectory
try {
    node .\open-jibo-link.js
}
finally {
    Pop-Location
}
