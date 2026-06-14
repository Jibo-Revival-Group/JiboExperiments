param(
    [string]$RegistryName,
    [string]$ImageName = "openjibo-cloud",
    [string]$Tag = "managed",
    [string]$DockerfilePath = "Dockerfile"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RegistryName)) {
    throw "RegistryName is required."
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\.."))
$resolvedDockerfilePath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $DockerfilePath))

if (-not (Test-Path -LiteralPath $resolvedDockerfilePath)) {
    throw "Could not find Dockerfile at $resolvedDockerfilePath"
}

$image = "$RegistryName.azurecr.io/$ImageName:$Tag"

Write-Host "Building managed Open Jibo image: $image"
docker build -f $resolvedDockerfilePath -t $image $repoRoot

Write-Host "Pushing managed Open Jibo image: $image"
docker push $image
