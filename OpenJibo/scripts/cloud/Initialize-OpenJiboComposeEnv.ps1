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
    if ($env:OPENJIBO_POSTGRES_PASSWORD) {
        $existingLines = [System.Collections.Generic.List[string]]::new()
        $existingLines.AddRange([string[]](Get-Content -LiteralPath $targetPath))
        $passwordLine = "OPENJIBO_POSTGRES_PASSWORD=$($env:OPENJIBO_POSTGRES_PASSWORD)"
        $foundPassword = $false

        for ($index = 0; $index -lt $existingLines.Count; $index++) {
            if ($existingLines[$index] -like "OPENJIBO_POSTGRES_PASSWORD=*") {
                $existingLines[$index] = $passwordLine
                $foundPassword = $true
                break
            }
        }

        if (-not $foundPassword) {
            $existingLines.Add("")
            $existingLines.Add($passwordLine)
        }

        Set-Content -LiteralPath $targetPath -Value $existingLines
    }
    return
}

Copy-Item -LiteralPath $templatePath -Destination $targetPath
if ($env:OPENJIBO_POSTGRES_PASSWORD) {
    Add-Content -LiteralPath $targetPath -Value ""
    Add-Content -LiteralPath $targetPath -Value "OPENJIBO_POSTGRES_PASSWORD=$($env:OPENJIBO_POSTGRES_PASSWORD)"
}
Write-Host "Created compose env from template: $targetPath"
