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
  "executionTarget": "server",
  "supportedLanguages": ["en"],
  "intentBindings": [
    {
      "intent": "requestWeather",
      "priority": 80,
      "match": { "entities": ["location", "date"] }
    }
  ]
}
```

The default runtime directory is `App_Data/Skills`. It can be overridden with
`OpenJibo:Skills:DirectoryPath`.
