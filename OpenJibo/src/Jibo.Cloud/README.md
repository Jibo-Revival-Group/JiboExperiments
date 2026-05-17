# Jibo.Cloud

## Summary

`Jibo.Cloud` is the replacement cloud layer for OpenJibo.

Its job is to restore the hosted services that physical Jibo devices still expect, while also becoming the bridge into a modern .NET runtime and future capabilities.

## Current Strategy

The project is deliberately split into two roles:

- `node/`
  Reverse-engineering oracle, discovery server, fixture source, and rapid protocol lab.
- `dotnet/`
  Stable hosted implementation intended for Azure deployment and long-term maintenance.

The Node server remains valuable, but it is no longer the target production architecture.

## First Production Goal

The first milestone is a stable hosted cloud that can support:

- token and session issuance
- account and robot identity flows needed for startup
- required HTTPS `X-Amz-Target` operations
- required WebSocket listen and proactive flows
- basic media and update metadata handling
- normalized handoff into OpenJibo runtime contracts

## Hosting Direction

The hosted deployment target is Azure:

- Azure App Service with WebSockets enabled
- Azure SQL as the system of record
- Azure Blob Storage for upload and update artifacts
- Azure Key Vault for secrets and certificates
- Application Insights for telemetry and diagnostics

Human-facing entry points will live on domains such as:

- `openjibo.com`
- `openjibo.ai`

Robot traffic may still arrive using legacy hostnames routed to the OpenJibo service.

## Azure Storage Wiring Sample

For local or hosted Blob-backed persistence, use the Azure sample config in:

- [appsettings.AzureBlob.sample.json](dotnet/src/Jibo.Cloud.Api/appsettings.AzureBlob.sample.json)

It shows the expected keys for:

- `OpenJibo:State:Backend`
- `OpenJibo:State:ConnectionString`
- `OpenJibo:PersonalMemory:Backend`
- `OpenJibo:PersonalMemory:ConnectionString`
- `OpenJibo:Media:Backend`
- `OpenJibo:Media:ConnectionString`

The connection string can also come from:

- `OPENJIBO_STATE_STORAGE_CONNECTION_STRING`
- `OPENJIBO_PERSONAL_MEMORY_STORAGE_CONNECTION_STRING`
- `OPENJIBO_MEDIA_STORAGE_CONNECTION_STRING`

For a real storage account, swap `UseDevelopmentStorage=true` with your Azure Storage connection string.

## Local Startup Note

To run the API with the Blob-backed sample config in Visual Studio or `dotnet run`, choose the
`Jibo.Cloud.Api.AzureBlob` launch profile.

The test project also has a matching `Jibo.Cloud.Tests.AzureBlob` profile so the smoke test can use
the same environment-variable shape when you run it from an IDE.

Equivalent environment variables:

```powershell
$env:OpenJibo__State__Backend = "AzureBlob"
$env:OpenJibo__State__ConnectionString = "UseDevelopmentStorage=true"
$env:OpenJibo__PersonalMemory__Backend = "AzureBlob"
$env:OpenJibo__PersonalMemory__ConnectionString = "UseDevelopmentStorage=true"
$env:OpenJibo__Media__Backend = "AzureBlob"
$env:OpenJibo__Media__ConnectionString = "UseDevelopmentStorage=true"
dotnet run --project dotnet/src/Jibo.Cloud.Api/Jibo.Cloud.Api.csproj
```

Replace `UseDevelopmentStorage=true` with your real storage account connection string when you move
from local emulation to Azure.

## Recovery Strategy

The first supported device path is:

```text
RCM + controlled DNS/TLS patching + hosted OpenJibo cloud
```

OTA remains important, but it is a later simplification layer after the hosted cloud is stable on real hardware.

## Supporting Docs

- [Protocol inventory](C:/Projects/JiboExperiments/OpenJibo/docs/protocol-inventory.md)
- [Support tiers](C:/Projects/JiboExperiments/OpenJibo/docs/support-tiers.md)
- [Device bootstrap path](C:/Projects/JiboExperiments/OpenJibo/docs/device-bootstrap.md)
