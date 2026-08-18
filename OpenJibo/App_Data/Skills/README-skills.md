# OpenJibo skill packages

Place one skill package in each child directory of this folder. Step 1 only reads
the package manifest; it does not execute package code yet.

Each package must contain a `manifest.json` with at least:

```json
{
  "skillId": "com.example.weather",
  "name": "Weather",
  "version": "1.0.0",
  "runtime": "dotnet",
  "routingFile": "routing.json",
  "executionTarget": "server",
  "supportedLanguages": ["en"]
}
```

The referenced `routing.json` contains the package's intent bindings. Small
packages may still provide `intentBindings` inline for convenience, but larger
packages should keep routing declarations in that separate file.

The default runtime directory is `App_Data/Skills`. It can be overridden with
`OpenJibo:Skills:DirectoryPath`.

The `openjibo-*` directories are built-in compatibility package manifests. They
describe the legacy skill verticals and are intentionally marked with
`"packageType": "builtin"` and `"adapter": "legacy"`. They remain visible in
the registry, but the new router will not activate them until their compatibility
adapter has been migrated and parity-checked.
