param(
    [Parameter(Mandatory = $true)]
    [string]$RobotRoot,
    [string]$OutputPath,
    [switch]$Strict
)

$ErrorActionPreference = "Stop"

function Resolve-CandidatePath {
    param(
        [string]$Root,
        [string[]]$RelativePaths
    )

    foreach ($relativePath in $RelativePaths) {
        $candidate = Join-Path $Root $relativePath
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Read-JsonFile {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Get-JsonField {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

$robotRootPath = (Resolve-Path -LiteralPath $RobotRoot).Path

$jetstreamPath = Resolve-CandidatePath -Root $robotRootPath -RelativePaths @(
    "etc\jibo-jetstream-service.json",
    "usr\local\etc\jibo-jetstream-service.json"
)

$credentialsPath = Resolve-CandidatePath -Root $robotRootPath -RelativePaths @(
    "var\jibo\credentials.json"
)

$oobeConfigPath = Resolve-CandidatePath -Root $robotRootPath -RelativePaths @(
    "skills\jibo\Jibo\Skills\oobe-config\config.json",
    "opt\jibo\Jibo\Skills\oobe-config\config.json"
)

$ssmPaths = @(
    (Join-Path $robotRootPath "etc\jibo-ssm")
    (Join-Path $robotRootPath "usr\local\etc\jibo-ssm")
) | Where-Object { Test-Path -LiteralPath $_ }

$jetstream = Read-JsonFile -Path $jetstreamPath
$credentials = Read-JsonFile -Path $credentialsPath
$oobeConfig = Read-JsonFile -Path $oobeConfigPath

$ssmFiles = foreach ($ssmPath in $ssmPaths) {
    Get-ChildItem -LiteralPath $ssmPath -Filter *.json -File -ErrorAction SilentlyContinue
}

$region = Get-JsonField -Object $credentials -Name "region"
$accessKeyId = Get-JsonField -Object $credentials -Name "accessKeyId"
$secretAccessKey = Get-JsonField -Object $credentials -Name "secretAccessKey"

$jetstreamRegionNames = @()
if ($jetstream -and $jetstream.PSObject.Properties.Name -contains "regions") {
    $jetstreamRegionNames = @(
        $jetstream.regions.PSObject.Properties.Name
    )
}

$audit = [pscustomobject]@{
    RobotRoot = $robotRootPath
    Files = [pscustomobject]@{
        Jetstream = $jetstreamPath
        Credentials = $credentialsPath
        OobeConfig = $oobeConfigPath
        SsmCount = @($ssmFiles).Count
    }
    Credentials = [pscustomobject]@{
        Region = $region
        AccessKeyIdPresent = -not [string]::IsNullOrWhiteSpace([string]$accessKeyId)
        SecretAccessKeyPresent = -not [string]::IsNullOrWhiteSpace([string]$secretAccessKey)
    }
    Jetstream = [pscustomobject]@{
        RegionNames = $jetstreamRegionNames
    }
    Oobe = [pscustomobject]@{
        ServerRegion = Get-JsonField -Object $oobeConfig -Name "serverRegion"
        OtaFilter = Get-JsonField -Object $oobeConfig -Name "otaFilter"
    }
    Recommendations = @(
        if ([string]::IsNullOrWhiteSpace($jetstreamPath)) { "Add or mount a jetstream region config file before conversion." }
        if ([string]::IsNullOrWhiteSpace($credentialsPath)) { "Locate credentials.json before attempting any mode switch." }
        if ([string]::IsNullOrWhiteSpace($oobeConfigPath)) { "Confirm the oobe-config bundle before wiring first-boot behavior." }
        if ([string]::IsNullOrWhiteSpace($region)) { "Region is not set yet; that needs to be recorded before any write helper runs." }
    ) | Where-Object { $_ }
}

$normalizedRecommendations = @($audit.Recommendations | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })

$audit | Add-Member -NotePropertyName CanProceed -NotePropertyValue ($normalizedRecommendations.Count -eq 0)
$audit | Add-Member -NotePropertyName BlockingIssues -NotePropertyValue $normalizedRecommendations

if ($Strict -and -not $audit.CanProceed) {
    throw "Conversion audit is not predictive-safe: $(@($audit.BlockingIssues) -join '; ')"
}

if ($OutputPath) {
    $resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
        [System.IO.Path]::GetFullPath($OutputPath)
    } else {
        [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
    }
    $audit | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput
    Write-Host "Saved conversion audit to $resolvedOutput"
}
else {
    $audit | Format-List
}
