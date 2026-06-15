param(
    [Parameter(Mandatory = $true)]
    [string]$RobotRoot,
    [string]$TargetMode = "open-jibo",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
$auditScript = Join-Path $scriptDir "Audit-OpenJiboConversion.ps1"
$tempAuditPath = Join-Path ([System.IO.Path]::GetTempPath()) ("openjibo-conversion-audit-{0}.json" -f ([Guid]::NewGuid().ToString("N")))

try {
    & $auditScript -RobotRoot $RobotRoot -OutputPath $tempAuditPath | Out-Null
    $audit = Get-Content -LiteralPath $tempAuditPath -Raw | ConvertFrom-Json
}
finally {
    if (Test-Path -LiteralPath $tempAuditPath) {
        Remove-Item -LiteralPath $tempAuditPath -Force -ErrorAction SilentlyContinue
    }
}

$existingMode = if ($audit.Credentials.Region) { [string]$audit.Credentials.Region } else { "unknown" }
$requiresAttention = @($audit.Recommendations).Count -gt 0

$proposedChanges = @(
    [pscustomobject]@{
        File = "/usr/local/etc/jibo-jetstream-service.json"
        Action = "add or update region-settings entries"
        Details = @(
            "preserve stock region where possible",
            "add target mode region entry for $TargetMode",
            "keep Open Jibo hostnames aligned with the documented bootstrap path"
        )
    }
    [pscustomobject]@{
        File = "/var/jibo/credentials.json"
        Action = "record the active region"
        Details = @(
            "save the current stock region before any switch",
            "switch the region field only after backups and validation"
        )
    }
    [pscustomobject]@{
        File = "/skills/jibo/Jibo/Skills/oobe-config/config.json"
        Action = "mark first-boot/OOBE state"
        Details = @(
            "keep the setup payload compatible with the classic QR decoder",
            "record first-boot pending state without destroying existing owner data"
        )
    }
)

$rollbackPlan = @(
    "restore the recorded jetstream config snapshot",
    "restore /var/jibo/credentials.json from the pre-conversion backup",
    "clear first-boot pending state if onboarding is abandoned",
    "leave the Open Jibo skill visible so the owner can retry conversion later"
)

$plan = [pscustomobject]@{
    RobotRoot = $audit.RobotRoot
    TargetMode = $TargetMode
    ExistingMode = $existingMode
    RequiresAttention = $requiresAttention
    AuditSummary = [pscustomobject]@{
        JetstreamPath = $audit.Files.Jetstream
        CredentialsPath = $audit.Files.Credentials
        OobeConfigPath = $audit.Files.OobeConfig
        SsmCount = $audit.Files.SsmCount
        Region = $audit.Credentials.Region
        OobeServerRegion = $audit.Oobe.ServerRegion
        OobeOtaFilter = $audit.Oobe.OtaFilter
        Recommendations = @($audit.Recommendations)
    }
    Backups = @(
        $audit.Files.Jetstream
        $audit.Files.Credentials
        $audit.Files.OobeConfig
    ) | Where-Object { $_ }
    ProposedChanges = $proposedChanges
    RollbackPlan = $rollbackPlan
    Preconditions = @(
        "verify the audit report is clean enough for the target device",
        "take a backup before any write helper runs",
        "confirm the conversion mode target with the owner"
    )
}

if ($OutputPath) {
    $resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        [System.IO.Path]::GetFullPath($OutputPath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
    }

    $plan | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolvedOutput
    Write-Host "Saved conversion plan to $resolvedOutput"
}
else {
    $plan | Format-List
}
