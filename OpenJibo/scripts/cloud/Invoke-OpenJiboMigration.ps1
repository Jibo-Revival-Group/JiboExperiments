param(
    [string]$ProjectPath = "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Jibo.Cloud.Migrations.csproj",
    [string]$ScriptsDirectory = "src/Jibo.Cloud/dotnet/src/Jibo.Cloud.Migrations/Migrations/PostgreSql",
    [ValidateSet("state", "personal-memory", "all")]
    [string]$Target = "all",
    [string]$StateConnectionString,
    [string]$PersonalMemoryConnectionString,
    [string]$MediaConnectionString,
    [string]$MediaContainer = "openjibo-media",
    [string]$ReplayObserverConnectionString,
    [switch]$ProvisionSigV4ReplayObserver,
    [string]$RuntimeUsageStateSchema,
    [string]$RuntimeUsageSourceLoginRole,
    [switch]$ProvisionRuntimeUsageDelivery,
    [switch]$ProvisionRuntimeUsageShadowSource,
    [switch]$ImportLegacyCloudState,
    [switch]$ImportLegacyPersonalMemory,
    [switch]$Verify,
    [switch]$Preview,
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedProjectPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ProjectPath))

if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "Could not find migration project at $resolvedProjectPath"
}

$arguments = @(
    "run",
    "--project", $resolvedProjectPath,
    "--",
    "--target", $Target
)

$resolvedScriptsDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ScriptsDirectory))
if (-not (Test-Path -LiteralPath $resolvedScriptsDirectory)) {
    throw "Could not find migration scripts at $resolvedScriptsDirectory"
}

$arguments += @("--scripts", $resolvedScriptsDirectory)

if (-not [string]::IsNullOrWhiteSpace($StateConnectionString)) {
    $arguments += @("--state-connection", $StateConnectionString)
}

if (-not [string]::IsNullOrWhiteSpace($PersonalMemoryConnectionString)) {
    $arguments += @("--memory-connection", $PersonalMemoryConnectionString)
}

if (-not [string]::IsNullOrWhiteSpace($MediaConnectionString)) {
    $arguments += @("--media-connection", $MediaConnectionString, "--media-container", $MediaContainer)
}

if (-not [string]::IsNullOrWhiteSpace($ReplayObserverConnectionString)) {
    $arguments += @("--replay-observer-connection", $ReplayObserverConnectionString)
}

if ($ProvisionSigV4ReplayObserver) {
    $arguments += "--provision-sigv4-replay-observer"
}

if ($ProvisionRuntimeUsageDelivery) {
    $arguments += "--provision-runtime-usage-delivery"
    if ([string]::IsNullOrWhiteSpace($RuntimeUsageStateSchema) -or
        [string]::IsNullOrWhiteSpace($RuntimeUsageSourceLoginRole)) {
        throw "Runtime usage delivery provisioning requires RuntimeUsageStateSchema and RuntimeUsageSourceLoginRole."
    }
    $arguments += @(
        "--runtime-usage-state-schema", $RuntimeUsageStateSchema,
        "--runtime-usage-source-login", $RuntimeUsageSourceLoginRole
    )
}

if ($ProvisionRuntimeUsageShadowSource) {
    if ([string]::IsNullOrWhiteSpace($RuntimeUsageStateSchema)) {
        throw "Runtime usage shadow-source provisioning requires RuntimeUsageStateSchema."
    }
    $arguments += @(
        "--provision-runtime-usage-shadow-source",
        "--runtime-usage-state-schema", $RuntimeUsageStateSchema
    )
}

if ($ImportLegacyCloudState) {
    $arguments += "--import-legacy-cloud-state"
}

if ($ImportLegacyPersonalMemory) {
    $arguments += "--import-legacy-personal-memory"
}

if ($Verify) {
    $arguments += "--verify"
}

if ($Preview) {
    $arguments += "--preview"
} else {
    $arguments += "--apply"
}

if ($VerboseOutput) {
    $arguments += "--verbose"
}

Write-Host "Running Open Jibo migrations for target '$Target'"
dotnet @arguments
