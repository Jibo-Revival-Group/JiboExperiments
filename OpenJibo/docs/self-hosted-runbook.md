# Self-Hosted OpenJibo Runbook

## Purpose

This is the single starting point for self-hosting the OpenJibo cloud with Docker Compose on your own hardware. Other docs cover specific pieces in more depth; this runbook tells you which one to read next for each step.

- [scripts/cloud/README.md](../scripts/cloud/README.md) — full inventory of every script in the repo, including the managed/Azure path. Use it as a reference, not a starting point.
- [docs/single-robot-http-self-hosting.md](single-robot-http-self-hosting.md) — deep dive on robot token/network overrides and the tokenless single-robot compatibility mode.
- [docs/local-cloud-quickstart.md](local-cloud-quickstart.md) — running the `.NET` cloud directly with `dotnet run` instead of Docker, useful for development.
- [docs/device-bootstrap.md](device-bootstrap.md) — pointing a physical Jibo at your self-hosted server.

## 1. Prerequisites

Install:

- Docker and Docker Compose (`docker compose` v2 CLI)
- PowerShell, if you prefer the `.ps1` scripts over the `.sh` equivalents

Everything else — .NET, ffmpeg, whisper.cpp — is built into the container image. You do not need to install any of those on the host.

## 2. Configure `.env`

From the `OpenJibo` repo root:

```powershell
.\scripts\cloud\Initialize-OpenJiboComposeEnv.ps1
```

```bash
./scripts/cloud/initialize-openjibo-compose-env.sh
```

This copies `.env.example` to `.env` if it does not already exist. Then edit `.env` and set at minimum:

- `OPENJIBO_POSTGRES_PASSWORD` — required; the stack will not start without it.
- `OPENJIBO_USER_ENCRYPT` / `OPENJIBO_USER_SALT` — replace the sample values. Do not change these after your first run; they encrypt user data and changing them makes existing data unrecoverable.
- `OPENJIBO_SEARCH_BACKEND` / `OPENJIBO_SEARCH_FALLBACK` — optional knowledge-search backend (Wolfram, ChatGPT, Ollama). Leave as `none` to disable.

Speech-to-text options (see [section 4](#4-speech-to-text-local-whisper-vs-azure-speech) below):

- `OPENJIBO_ENABLE_LOCAL_WHISPER` — defaults to `true`. Set to `false` if you only plan to use Azure Speech.
- `OPENJIBO_WHISPER_MODEL` — defaults to `base.en`. Any model name accepted by whisper.cpp's `download-ggml-model.sh`.

## 3. Build and start the stack

```powershell
.\scripts\cloud\Invoke-OpenJiboSelfHostedStack.ps1 -RunMigration
```

```bash
./scripts/cloud/invoke-openjibo-self-hosted-stack.sh --run-migration
```

This builds the `api`/`migrate` image (installing whisper.cpp and its model as part of the build, unless disabled), starts PostgreSQL, applies migrations, and starts the API on port `8080`.

- Use `-SkipBuild` / `--skip-build` on later runs if you have not changed the Dockerfile, `.env` whisper settings, or source.
- Omit `-RunMigration` / `--run-migration` once your schema is already up to date; the compose file only prevents `api` from starting before `migrate` completes on first bring-up.

Rebuild is required any time you change `OPENJIBO_ENABLE_LOCAL_WHISPER` or `OPENJIBO_WHISPER_MODEL`, since those are Docker build arguments, not just runtime environment variables.

## 4. Speech-to-text: local Whisper vs Azure Speech

By default, the container image builds [whisper.cpp](https://github.com/ggml-org/whisper.cpp) from source and downloads the `base.en` model at build time, landing both at `/usr/bin/whisper.cpp/` inside the image. `docker-compose.yml` wires this up automatically:

- `OpenJibo__Stt__EnableLocalWhisperCpp=true`
- `OPENJIBO_STT_WHISPER_CLI_PATH=/usr/bin/whisper.cpp/build/bin/whisper-cli`
- `OPENJIBO_STT_WHISPER_MODEL_PATH=/usr/bin/whisper.cpp/models/ggml-<model>.bin`

No extra setup is needed for local transcription to work out of the box.

To use a different model, set `OPENJIBO_WHISPER_MODEL` in `.env` (e.g. `small.en`) and rebuild.

To skip local Whisper entirely and rely only on Azure Speech (smaller image, faster build, no local CPU transcription cost), set in `.env`:

```dotenv
OPENJIBO_ENABLE_LOCAL_WHISPER=false
```

Then also set the Azure Speech settings so a working STT path is actually available:

```dotenv
OpenJibo__Stt__EnableAzureSpeech=true
OpenJibo__Stt__AzureSpeechRegion=<your-region>
OpenJibo__Stt__AzureSpeechSubscriptionKey=<your-key>
```

Rebuild after changing `OPENJIBO_ENABLE_LOCAL_WHISPER` since it controls which Docker build stage runs.

## 5. Verify the stack

```powershell
.\scripts\cloud\Invoke-CloudSmoke.ps1 -BaseUrl http://localhost:8080 -TargetMode open-jibo-self-hosted
```

```bash
./scripts/cloud/invoke-cloud-smoke.sh --base-url http://localhost:8080 --target-mode open-jibo-self-hosted
```

Or just hit the health check directly:

```powershell
Invoke-RestMethod http://localhost:8080/health
```

## 6. Point a physical Jibo at the stack

See [docs/single-robot-http-self-hosting.md](single-robot-http-self-hosting.md) for the robot-side override JSON, the tokenless single-robot compatibility flag, and common `WebSocket Exception: Not authorized` / DNS troubleshooting. See [docs/device-bootstrap.md](device-bootstrap.md) for the device bootstrap steps themselves.

## Troubleshooting

- **`api` logs show a whisper-cli launch failure or missing model** — confirm the image was actually rebuilt after any `.env` whisper changes (`-SkipBuild`/`--skip-build` skips this). Check `docker compose logs api` for the resolved paths.
- **Build is slow or fails compiling whisper.cpp** — set `OPENJIBO_ENABLE_LOCAL_WHISPER=false` and use Azure Speech instead, or lower `OPENJIBO_WHISPER_MODEL` to a smaller model.
- **`migrate` never completes / `api` never starts** — check `docker compose logs postgres` and `docker compose logs migrate`; the `api` service depends on both `postgres` (healthy) and `migrate` (completed successfully).
- **Encryption errors after restoring data** — `OPENJIBO_USER_ENCRYPT`/`OPENJIBO_USER_SALT` must exactly match the values used when the data was written.
