param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot
)

$ErrorActionPreference = "Stop"

function Convert-ToGitBashPath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return $Path }
    $normalized = $Path -replace '\\', '/'
    if ($normalized -match '^[A-Za-z]:/') {
        return '/' + $normalized.Substring(0, 1).ToLowerInvariant() + $normalized.Substring(2)
    }

    return $normalized
}

function Escape-BashSingleQuoted {
    param([string]$Text)
    return ($Text -replace "'", "'\''")
}

$scriptDir = Convert-ToGitBashPath $PSScriptRoot
$outputRootUnix = Convert-ToGitBashPath $OutputRoot

$bash = if (Test-Path "C:\Program Files\Git\bin\bash.exe") {
    "C:\Program Files\Git\bin\bash.exe"
} elseif (Test-Path "C:\Program Files\Git\usr\bin\bash.exe") {
    "C:\Program Files\Git\usr\bin\bash.exe"
} else {
    throw "Unable to locate Git Bash. Use the Linux shell helper directly."
}

$command = "cd '$(Escape-BashSingleQuoted $scriptDir)' && ./inspect-linux-filesystem-demo.sh --output-root '$(Escape-BashSingleQuoted $outputRootUnix)'"

& $bash -lc $command
