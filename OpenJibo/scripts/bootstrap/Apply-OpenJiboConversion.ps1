param(
    [Parameter(Mandatory = $true)]
    [string]$RobotRoot,
    [Parameter(Mandatory = $true)]
    [string]$PlanPath,
    [string]$TargetMode = "open-jibo",
    [string]$OutputPath,
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
$planPathUnix = Convert-ToGitBashPath $PlanPath
$outputPathUnix = Convert-ToGitBashPath $OutputPath

$bash = if (Test-Path "C:\Program Files\Git\bin\bash.exe") {
    "C:\Program Files\Git\bin\bash.exe"
} elseif (Test-Path "C:\Program Files\Git\usr\bin\bash.exe") {
    "C:\Program Files\Git\usr\bin\bash.exe"
} else {
    throw "Unable to locate Git Bash. Use the Linux shell helper directly."
}

$command = "cd '$(Escape-BashSingleQuoted $scriptDir)' && ./apply-openjibo-conversion.sh --robot-root '$(Escape-BashSingleQuoted $robotRootUnix)' --plan-path '$(Escape-BashSingleQuoted $planPathUnix)' --target-mode '$TargetMode'"
if ($OutputPathUnix) {
    $command += " --output-path '$(Escape-BashSingleQuoted $outputPathUnix)'"
}
if ($Strict) {
    $command += " --strict"
}

& $bash -lc $command
