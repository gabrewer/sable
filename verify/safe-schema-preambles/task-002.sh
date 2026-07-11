#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

SABLE_RUN_POSTGRES_SMOKE=1 dotnet test \
  tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj \
  --filter "FullyQualifiedName~SafeSchemaPreamblePostgresSmokeTests"
