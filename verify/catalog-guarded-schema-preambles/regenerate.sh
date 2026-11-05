#!/usr/bin/env bash
set -euo pipefail
umask 077

if [[ "${SABLE_REGENERATE_CATALOG_GUARDED_SAMPLE:-}" != "1" ]]; then
  echo "Set SABLE_REGENERATE_CATALOG_GUARDED_SAMPLE=1 to regenerate this sample." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
sample="samples/Sable.Samples.CatalogGuardedSchemaPreamble"
output="$sample/sable"
mkdir -p .pi/tmp
temporary="$(mktemp -d "$repo_root/.pi/tmp/catalog-guarded-generation.XXXXXX")"
backup="$temporary/previous-sable"
generation_started=false
complete=false
database="sable_catalog_$(python -c 'import uuid; print(uuid.uuid4().hex)')"

cleanup() {
  status=$?
  trap - EXIT
  if ! python - "$database" <<'PY'
import json, subprocess, sys
owned_database = 'POSTGRES_DB=' + sys.argv[1]
try:
    ids = subprocess.run(['docker', 'ps', '-aq', '--filter', 'ancestor=postgres:15.1'],
                         capture_output=True, text=True, check=True, timeout=20).stdout.split()
    for identity in ids:
        details = json.loads(subprocess.run(['docker', 'inspect', identity], capture_output=True,
                            text=True, check=True, timeout=20).stdout)[0]
        if owned_database in details.get('Config', {}).get('Env', []):
            subprocess.run(['docker', 'rm', '-f', identity], capture_output=True, check=True, timeout=30)
except Exception:
    print('Could not verify cleanup of owned generation resources.', file=sys.stderr)
    sys.exit(1)
PY
  then
    status=1
    complete=false
  fi
  if [[ "$complete" != "true" ]]; then
    if [[ -d "$backup" ]]; then
      rm -rf "$output"
      mv "$backup" "$output"
    elif [[ "$generation_started" == "true" ]]; then
      rm -rf "$output"
    fi
  fi
  rm -rf "$temporary"
  exit "$status"
}
trap cleanup EXIT

python - "$temporary/container-options.json" "$database" <<'PY'
import json, secrets, socket, sys
with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0))
    port = listener.getsockname()[1]
password = secrets.token_hex(24)
database = sys.argv[2]
options = {
    'Image': 'postgres:15.1',
    'PortBindings': [{'HostPort': port, 'ContainerPort': 5432}],
    'EnvironmentVariables': {'PGPORT': '5432', 'POSTGRES_DB': database,
                             'POSTGRES_USER': 'postgres', 'POSTGRES_PASSWORD': password},
    'ConnectionString': f'Host=127.0.0.1;Port={port};Username=postgres;Password={password};Database={database};Pooling=false'
}
with open(sys.argv[1], 'w', encoding='utf-8') as output:
    json.dump(options, output)
PY

if [[ -d "$output" ]]; then
  mv "$output" "$backup"
fi
generation_started=true

python - "$temporary/container-options.json" <<'PY'
import json, os, signal, subprocess, sys
options_file = sys.argv[1]
with open(options_file, encoding='utf-8') as source:
    options = json.load(source)
environment = os.environ.copy()
environment.pop('SABLE_CATALOG_GUARDED_SAMPLE_INCLUDE_PENDING_CHANGE', None)
environment.pop('SABLE_CONNECTION_STRING_OVERRIDE', None)
timeout = int(environment.get('SABLE_CATALOG_GUARDED_GENERATION_TIMEOUT_SECONDS', '180'))
if not 1 <= timeout <= 180:
    raise ValueError('Generation timeout must be between 1 and 180 seconds.')
arguments = ['dotnet', 'run', '--project', 'src/Sable.Cli/Sable.Cli.csproj', '--', 'init',
             '--project', 'samples/Sable.Samples.CatalogGuardedSchemaPreamble/Sable.Samples.CatalogGuardedSchemaPreamble.csproj',
             '--database', 'marten://store/', '--schema', 'catalog_guarded_schema_preamble_sample',
             '--container-options', options_file]
process = subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                           env=environment, start_new_session=os.name != 'nt')
try:
    output, _ = process.communicate(timeout=timeout)
    status = process.returncode
except subprocess.TimeoutExpired:
    if os.name == 'nt':
        subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=20)
    else:
        os.killpg(process.pid, signal.SIGKILL)
    output, _ = process.communicate(timeout=20)
    status = 124
    print('Owned sample generation timed out.', file=sys.stderr)
text = output.decode('utf-8', errors='replace')
text = text.replace(options['ConnectionString'], '<redacted-connection>')
text = text.replace(options['EnvironmentVariables']['POSTGRES_PASSWORD'], '<redacted>')
print(text)
sys.exit(status)
PY

python - "$output/Marten" <<'PY'
from pathlib import Path
import re, sys
root = Path(sys.argv[1])
initials = list((root / 'migrations').glob('*_Initial.sql'))
if len(initials) != 1:
    raise RuntimeError('Sable did not produce exactly one Initial migration.')
source = initials[0].read_text(encoding='utf-8-sig')
required = [r'pg_catalog\s*\.\s*pg_namespace', r"nspname\s*=\s*'catalog_guarded_schema_preamble_sample'",
            r'WHEN\s+duplicate_schema\s+THEN\s+NULL', r'WHEN\s+unique_violation\s+THEN\s+NULL',
            r'CREATE\s+OR\s+REPLACE\s+FUNCTION']
if not all(re.search(pattern, source, flags=re.IGNORECASE) for pattern in required):
    raise RuntimeError('Pinned producer did not emit the approved guarded schema/function shape.')
print('Validated authentic guarded Initial migration; no generated SQL was edited.')
PY
complete=true
echo "Generated $output through Sable. Review artifacts without editing them."
