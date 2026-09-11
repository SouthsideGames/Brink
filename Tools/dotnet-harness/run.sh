#!/usr/bin/env bash
# Run the edit-mode suite without Unity. See README.md — this is a verification
# aid, not a replacement for `Tools/run-suite.sh`.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd "$(dirname "$0")/tests"
dotnet test "$@"
