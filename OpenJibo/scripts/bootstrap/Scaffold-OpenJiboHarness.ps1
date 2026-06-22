param(
    [Parameter(Mandatory = $true)]
    [string]$SourceRoot,
    [Parameter(Mandatory = $true)]
    [string]$OverlayRoot,
    [string]$OutputPath,
    [switch]$Clean
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
$sourceRootUnix = Convert-ToGitBashPath $SourceRoot
$overlayRootUnix = Convert-ToGitBashPath $OverlayRoot
$outputPathUnix = Convert-ToGitBashPath $OutputPath

$bash = if (Test-Path "C:\Program Files\Git\bin\bash.exe") {
    "C:\Program Files\Git\bin\bash.exe"
} elseif (Test-Path "C:\Program Files\Git\usr\bin\bash.exe") {
    "C:\Program Files\Git\usr\bin\bash.exe"
} else {
    throw "Unable to locate Git Bash. Use the Linux shell helper directly."
}

$command = "cd '$(Escape-BashSingleQuoted $scriptDir)' && ./scaffold-openjibo-harness.sh --source-root '$(Escape-BashSingleQuoted $sourceRootUnix)' --overlay-root '$(Escape-BashSingleQuoted $overlayRootUnix)'"
if ($outputPathUnix) {
    $command += " --output-path '$(Escape-BashSingleQuoted $outputPathUnix)'"
}
if ($Clean) {
    $command += " --clean"
}

& $bash -lc $command
