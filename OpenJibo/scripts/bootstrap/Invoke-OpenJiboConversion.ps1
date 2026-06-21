param(
    [Parameter(Mandatory = $true)]
    [string]$RobotRoot,
    [string]$TargetMode = "open-jibo",
    [string]$OutputDirectory,
    [switch]$Apply,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$auditScript = Join-Path $scriptDir "Audit-OpenJiboConversion.ps1"
$planScript = Join-Path $scriptDir "Plan-OpenJiboConversion.ps1"
$applyScript = Join-Path $scriptDir "Apply-OpenJiboConversion.ps1"

if (-not (Test-Path -LiteralPath $auditScript)) {
    throw "Missing audit helper at $auditScript"
}

if (-not (Test-Path -LiteralPath $planScript)) {
    throw "Missing plan helper at $planScript"
}

if ($Apply -and -not (Test-Path -LiteralPath $applyScript)) {
    throw "Apply helper is missing at $applyScript"
}

$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    Join-Path ([System.IO.Path]::GetTempPath()) ("openjibo-conversion-{0}" -f ([Guid]::NewGuid().ToString("N")))
}
elseif ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputDirectory))
}

New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$auditPath = Join-Path $resolvedOutputDirectory "conversion-audit.json"
$planPath = Join-Path $resolvedOutputDirectory "conversion-plan.json"
$applyPath = Join-Path $resolvedOutputDirectory "conversion-apply.json"

& $auditScript -RobotRoot $RobotRoot -OutputPath $auditPath -Strict:$Strict | Out-Null
& $planScript -RobotRoot $RobotRoot -TargetMode $TargetMode -OutputPath $planPath -Strict:$Strict | Out-Null

$summary = [pscustomobject]@{
    RobotRoot = (Resolve-Path -LiteralPath $RobotRoot).Path
    TargetMode = $TargetMode
    OutputDirectory = $resolvedOutputDirectory
    AuditPath = $auditPath
    PlanPath = $planPath
    Applied = $false
}

if ($Apply) {
    & $applyScript -RobotRoot $RobotRoot -TargetMode $TargetMode -PlanPath $planPath -OutputPath $applyPath -Strict:$Strict | Out-Null
    $summary | Add-Member -NotePropertyName Applied -NotePropertyValue $true -Force
    $summary | Add-Member -NotePropertyName ApplyPath -NotePropertyValue $applyPath
}

$summary | Format-List
