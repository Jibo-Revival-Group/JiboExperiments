param(
    [Parameter(Mandatory = $true)]
    [string]$RobotRoot,
    [string]$TargetMode = "open-jibo",
    [string]$ApiHostname = "api.openjibo.com",
    [string]$HubHostname = "",
    [string]$OutputDirectory,
    [switch]$Apply,
    [switch]$Strict
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
$robotRootUnix = Convert-ToGitBashPath $RobotRoot
$outputDirectoryUnix = Convert-ToGitBashPath $OutputDirectory

$bash = if (Test-Path "C:\Program Files\Git\bin\bash.exe") {
    "C:\Program Files\Git\bin\bash.exe"
} elseif (Test-Path "C:\Program Files\Git\usr\bin\bash.exe") {
    "C:\Program Files\Git\usr\bin\bash.exe"
} else {
    throw "Unable to locate Git Bash. Use the Linux shell helper directly."
}

$command = "cd '$(Escape-BashSingleQuoted $scriptDir)' && ./invoke-openjibo-conversion.sh --robot-root '$(Escape-BashSingleQuoted $robotRootUnix)' --target-mode '$TargetMode' --api-hostname '$(Escape-BashSingleQuoted $ApiHostname)'"
if (-not [string]::IsNullOrWhiteSpace($HubHostname)) {
    $command += " --hub-hostname '$(Escape-BashSingleQuoted $HubHostname)'"
}
if ($outputDirectoryUnix) {
    $command += " --output-directory '$(Escape-BashSingleQuoted $outputDirectoryUnix)'"
}
if ($Apply) {
    $command += " --apply"
}
if ($Strict) {
    $command += " --strict"
}

& $bash -lc $command