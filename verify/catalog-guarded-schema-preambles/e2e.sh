#!/usr/bin/env bash
set -euo pipefail
umask 077
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
mkdir -p .pi/tmp
temporary="$(mktemp -d "$repo_root/.pi/tmp/catalog-e2e.XXXXXX")"
run_id="$(python -c 'import uuid; print(uuid.uuid4().hex)')"
cleanup() {
  status=$?
  trap - EXIT
  if ! python - "$run_id" "$repo_root" <<'PY'
import json, shutil, subprocess, sys, tempfile
from pathlib import Path
run_id, repository = sys.argv[1], Path(sys.argv[2])
try:
    ids = subprocess.run(['docker', 'ps', '-aq', '--filter', 'ancestor=postgres:15.1'],
                         capture_output=True, text=True, check=True, timeout=20).stdout.split()
    for identity in ids:
        config = json.loads(subprocess.run(['docker', 'inspect', identity], capture_output=True,
                            text=True, check=True, timeout=20).stdout)[0]['Config']
        if (config.get('Labels') or {}).get('sable-verification-run') == run_id or \
                'SABLE_VERIFICATION_RUN_ID=' + run_id in config.get('Env', []):
            subprocess.run(['docker', 'rm', '-f', identity], capture_output=True, check=True, timeout=30)
    for directory in (repository / 'samples').glob('.catalog-guarded-e2e-' + run_id + '-*'):
        shutil.rmtree(directory)
    for directory in Path(tempfile.gettempdir()).glob('sable-catalog-cli-' + run_id + '-*'):
        shutil.rmtree(directory)
except Exception:
    print('Could not verify cleanup of owned E2E resources.', file=sys.stderr)
    sys.exit(1)
PY
  then
    status=1
  fi
  rm -rf "$temporary"
  exit "$status"
}
trap cleanup EXIT

python - "$run_id" "$temporary" <<'PY'
import os, signal, subprocess, sys
run_id, results = sys.argv[1:]
environment = os.environ.copy()
environment['SABLE_SCHEMA_PREAMBLE_VERIFICATION_RUN_ID'] = run_id
environment['SABLE_RUN_CATALOG_GUARDED_E2E'] = '1'
arguments = ['dotnet', 'test', 'tests/Sable.Cli.Tests/Sable.Cli.Tests.csproj',
             '--filter', 'FullyQualifiedName~CatalogGuardedSchemaPreambleEndToEndTests',
             '--logger', 'trx;LogFileName=e2e.trx', '--results-directory', results]
process = subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                           env=environment, start_new_session=os.name != 'nt')
try:
    output, _ = process.communicate(timeout=600)
    status = process.returncode
except subprocess.TimeoutExpired:
    if os.name == 'nt':
        subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=20)
    else:
        os.killpg(process.pid, signal.SIGKILL)
    output, _ = process.communicate(timeout=20)
    status = 124
    print('Owned end-to-end verification timed out.', file=sys.stderr)
print(output.decode('utf-8', errors='replace'))
sys.exit(status)
PY

python - "$temporary/e2e.trx" <<'PY'
import sys, xml.etree.ElementTree as ET
counters = ET.parse(sys.argv[1]).getroot().find('.//{*}Counters')
if counters is None:
    raise RuntimeError('Missing test execution counters.')
executed, passed, total = (int(counters.get(name, '0')) for name in ('executed', 'passed', 'total'))
if executed < 2 or passed != executed or total != executed:
    raise RuntimeError('Expected both owned E2E cases to execute and pass, not skip.')
print(f'PASS: {passed} full CLI workflow cases executed; original sources and generated baselines remain unchanged.')
PY
