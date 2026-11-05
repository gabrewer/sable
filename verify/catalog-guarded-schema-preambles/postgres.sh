#!/usr/bin/env bash
set -euo pipefail
umask 077
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
mkdir -p .pi/tmp
temporary="$(mktemp -d "$repo_root/.pi/tmp/catalog-postgres.XXXXXX")"
run_id="$(python -c 'import uuid; print(uuid.uuid4().hex)')"
cleanup() {
  status=$?
  trap - EXIT
  if ids="$(docker ps -aq --filter "label=sable-verification-run=$run_id")"; then
    while IFS= read -r identity; do
      if [[ -n "$identity" ]]; then
        docker rm -f "$identity" >/dev/null || status=1
      fi
    done <<< "$ids"
  else
    status=1
  fi
  rm -rf "$temporary"
  exit "$status"
}
trap cleanup EXIT

timeout --kill-after=10s 600s env \
  SABLE_SCHEMA_PREAMBLE_VERIFICATION_RUN_ID="$run_id" \
  SABLE_RUN_CATALOG_GUARDED_POSTGRES=1 \
  SABLE_RUN_POSTGRES_SMOKE=1 \
  dotnet test tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj \
  --filter "FullyQualifiedName~CatalogGuardedSchemaPreamblePostgresTests|FullyQualifiedName~SafeSchemaPreamblePostgresSmokeTests" \
  --logger "trx;LogFileName=postgres.trx" --results-directory "$temporary"

python - "$temporary/postgres.trx" <<'PY'
import sys, xml.etree.ElementTree as ET
counters = ET.parse(sys.argv[1]).getroot().find('.//{*}Counters')
if counters is None:
    raise RuntimeError('Missing test execution counters.')
executed, passed = int(counters.get('executed', '0')), int(counters.get('passed', '0'))
if executed < 5 or passed != executed:
    raise RuntimeError('Expected all five owned PostgreSQL cases to execute and pass, not skip.')
print(f'PASS: {passed} CLI-driven disposable PostgreSQL cases executed; discovery/skips are not evidence.')
PY
