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

function New-RandomHex([int]$ByteCount) {
    $bytes = New-Object byte[] $ByteCount
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $random.GetBytes($bytes)
    }
    finally {
        $random.Dispose()
    }
    return [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant()
}

$templateText = [System.IO.File]::ReadAllText($templatePath)
$lines = [System.Text.RegularExpressions.Regex]::Split($templateText, "\r\n|\n|\r")
$encryptMatches = @($lines | Where-Object { $_ -match '^OPENJIBO_USER_ENCRYPT=' })
$saltMatches = @($lines | Where-Object { $_ -match '^OPENJIBO_USER_SALT=' })
$noncanonicalMatches = @($lines | Where-Object {
    $_ -match '^\s*(export\s+)?OPENJIBO_USER_(ENCRYPT|SALT)(\s|=)' -and
    $_ -notmatch '^OPENJIBO_USER_(ENCRYPT|SALT)='
})
if ($encryptMatches.Count -ne 1 -or $saltMatches.Count -ne 1 -or $noncanonicalMatches.Count -ne 0) {
    throw 'Compose env template must contain exactly one user encryption key and salt assignment.'
}

$encryptionSecret = New-RandomHex 32
$saltSecret = New-RandomHex 16
$renderedLines = foreach ($line in $lines) {
    if ($line -match '^OPENJIBO_USER_ENCRYPT=') {
        "OPENJIBO_USER_ENCRYPT=$encryptionSecret"
    }
    elseif ($line -match '^OPENJIBO_USER_SALT=') {
        "OPENJIBO_USER_SALT=$saltSecret"
    }
    else {
        $line
    }
}
$newContents = $renderedLines -join [Environment]::NewLine
if ($env:OPENJIBO_POSTGRES_PASSWORD) {
    $newContents += [Environment]::NewLine + [Environment]::NewLine +
        "OPENJIBO_POSTGRES_PASSWORD=$($env:OPENJIBO_POSTGRES_PASSWORD)" + [Environment]::NewLine
}

$tempPath = "$targetPath.tmp.$PID.$([guid]::NewGuid().ToString('N'))"
try {
    $encoding = [System.Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($newContents)
    $stream = [System.IO.File]::Open($tempPath, [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Unix) {
            & chmod 600 $tempPath
            if ($LASTEXITCODE -ne 0) {
                throw 'Could not restrict permissions on staged compose env.'
            }
        }
        $stream.Write($bytes, 0, $bytes.Length)
        $stream.Flush()
    }
    finally {
        $stream.Dispose()
    }
    [System.IO.File]::Move($tempPath, $targetPath)
}
catch [System.IO.IOException] {
    throw 'Could not install compose env; an existing file was preserved.'
}
finally {
    if (Test-Path -LiteralPath $tempPath) {
        Remove-Item -LiteralPath $tempPath -Force
    }
}
Write-Host "Created compose env from template: $targetPath"
