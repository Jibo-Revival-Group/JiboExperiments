$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Initialize-OpenJiboComposeEnv.ps1'
$tempParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$tempRoot = Join-Path $tempParent ("openjibo-env-test-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$oldPassword = $env:OPENJIBO_POSTGRES_PASSWORD
$env:OPENJIBO_POSTGRES_PASSWORD = $null

function Assert([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "FAIL: $Message" }
}
function New-Fixture([string]$Name) {
    $root = Join-Path $tempRoot $Name
    New-Item -ItemType Directory -Path (Join-Path $root 'scripts/cloud') -Force | Out-Null
    Copy-Item -LiteralPath $scriptPath -Destination (Join-Path $root 'scripts/cloud/Initialize-OpenJiboComposeEnv.ps1')
    [IO.File]::WriteAllText((Join-Path $root '.env.example'), "APP_MODE=fixture`nOPENJIBO_USER_ENCRYPT=template-marker-encrypt`nOPENJIBO_USER_SALT=template-marker-salt`n")
    return $root
}
function Get-EnvValue([string]$Path, [string]$Key) {
    $line = Get-Content -LiteralPath $Path | Where-Object { $_ -like "$Key=*" } | Select-Object -First 1
    if ($null -eq $line) { return $null }
    return $line.Substring($Key.Length + 1)
}

try {
    $first = New-Fixture 'first'
    $second = New-Fixture 'second'
    $firstOutput = (& (Join-Path $first 'scripts/cloud/Initialize-OpenJiboComposeEnv.ps1') -RepoRoot $first *>&1 | Out-String)
    $secondOutput = (& (Join-Path $second 'scripts/cloud/Initialize-OpenJiboComposeEnv.ps1') -RepoRoot $second *>&1 | Out-String)
    $firstKey = Get-EnvValue (Join-Path $first '.env') 'OPENJIBO_USER_ENCRYPT'
    $firstSalt = Get-EnvValue (Join-Path $first '.env') 'OPENJIBO_USER_SALT'
    $secondKey = Get-EnvValue (Join-Path $second '.env') 'OPENJIBO_USER_ENCRYPT'
    $secondSalt = Get-EnvValue (Join-Path $second '.env') 'OPENJIBO_USER_SALT'
    Assert ($firstKey -match '^[0-9a-f]{64}$' -and $firstSalt -match '^[0-9a-f]{32}$') 'generated secret format incorrect'
    Assert ($firstKey -cne $secondKey -and $firstSalt -cne $secondSalt) 'fresh initializations reused a secret'
    Assert (-not $firstOutput.Contains($firstKey) -and -not $firstOutput.Contains($firstSalt) -and
        -not $secondOutput.Contains($secondKey) -and -not $secondOutput.Contains($secondSalt)) 'initializer printed generated secrets'
    Assert (-not (Get-Content (Join-Path $first '.env') -Raw).Contains('template-marker')) 'template marker was copied'

    $existing = New-Fixture 'existing'
    [IO.File]::WriteAllText((Join-Path $existing '.env'), "OPENJIBO_USER_ENCRYPT=existing-ciphertext`nOPENJIBO_USER_SALT=existing-salt`nOPENJIBO_POSTGRES_PASSWORD=old-db`n")
    $env:OPENJIBO_POSTGRES_PASSWORD = 'new-db'
    & (Join-Path $existing 'scripts/cloud/Initialize-OpenJiboComposeEnv.ps1') -RepoRoot $existing | Out-Null
    $existingText = Get-Content (Join-Path $existing '.env') -Raw
    Assert ($existingText.Contains('OPENJIBO_USER_ENCRYPT=existing-ciphertext')) 'existing encryption value changed'
    Assert ($existingText.Contains('OPENJIBO_USER_SALT=existing-salt')) 'existing salt changed'
    Assert ($existingText.Contains('OPENJIBO_POSTGRES_PASSWORD=new-db')) 'PostgreSQL override behavior changed'

    [IO.File]::WriteAllText((Join-Path $existing '.env.example'), "malformed template without required assignments`n")
    & (Join-Path $existing 'scripts/cloud/Initialize-OpenJiboComposeEnv.ps1') -RepoRoot $existing | Out-Null
    $existingTextAfterMalformedTemplate = Get-Content (Join-Path $existing '.env') -Raw
    Assert ($existingTextAfterMalformedTemplate.Contains('OPENJIBO_USER_ENCRYPT=existing-ciphertext') -and
        $existingTextAfterMalformedTemplate.Contains('OPENJIBO_USER_SALT=existing-salt')) 'existing env changed when template assignments were malformed'

    foreach ($kind in @('missing', 'duplicate', 'noncanonical')) {
        $fixture = New-Fixture "bad-$kind"
        if ($kind -eq 'missing') { $content = "OPENJIBO_USER_ENCRYPT=a`n" }
        elseif ($kind -eq 'duplicate') { $content = "OPENJIBO_USER_ENCRYPT=a`nOPENJIBO_USER_ENCRYPT=b`nOPENJIBO_USER_SALT=s`n" }
        else { $content = "OPENJIBO_USER_ENCRYPT=a`nOPENJIBO_USER_SALT=s`n export OPENJIBO_USER_ENCRYPT = override`n" }
        [IO.File]::WriteAllText((Join-Path $fixture '.env.example'), $content)
        $failed = $false
        try { & (Join-Path $fixture 'scripts/cloud/Initialize-OpenJiboComposeEnv.ps1') -RepoRoot $fixture 2>$null }
        catch { $failed = $true }
        Assert $failed "$kind template unexpectedly succeeded"
        Assert (-not (Test-Path (Join-Path $fixture '.env'))) "$kind template left final .env"
    }
    Write-Output 'PASS: PowerShell compose env initializer offline tests'
}
finally {
    $env:OPENJIBO_POSTGRES_PASSWORD = $oldPassword
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $tempRoot).Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
        $expectedPrefix = $tempParent + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedTemp.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            (Split-Path -Leaf $resolvedTemp) -notmatch '^openjibo-env-test-[0-9a-f]{32}$') {
            throw 'Refusing cleanup outside validated test temp directory.'
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
