#!/bin/sh
set -eu

# Migration / one-shot commands pass args through (see docker-compose migrate service).
# Warm whisper-server is started by WhisperServerHostedService inside the API process
# (same path as `dotnet run` / published binaries).
if [ "$#" -gt 0 ]; then
  exec dotnet "$@"
fi

exec dotnet /app/api/Jibo.Cloud.Api.dll
