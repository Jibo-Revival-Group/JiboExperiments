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

if (-not (Test-Path -LiteralPath $PlanPath)) {
    throw "Plan file not found at $PlanPath"
}

$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json

if ($Strict -and -not $plan.CanApply) {
    $issues = @($plan.AuditSummary.Recommendations | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
    throw "Conversion apply is not safe to run: $($issues -join '; ')"
}

$applyManifest = [pscustomobject]@{
    RobotRoot = (Resolve-Path -LiteralPath $RobotRoot).Path
    TargetMode = $TargetMode
    SourcePlan = $PlanPath
    CanApply = [bool]$plan.CanApply
    Backups = @($plan.Backups)
    ProposedChanges = @($plan.ProposedChanges)
    RollbackPlan = @($plan.RollbackPlan)
    Notes = @(
        "This helper currently records an apply manifest and keeps the actual robot write step gated behind the predictive audit."
        "It is safe to run on a staged robot root because it does not modify robot files yet."
    )
}

if ($OutputPath) {
    $resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        [System.IO.Path]::GetFullPath($OutputPath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
    }

    $applyManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolvedOutput
    Write-Host "Saved conversion apply manifest to $resolvedOutput"
}
else {
    $applyManifest | Format-List
}
