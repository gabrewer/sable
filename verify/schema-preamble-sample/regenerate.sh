#!/usr/bin/env bash
set -euo pipefail

if [[ "${SABLE_REGENERATE_SCHEMA_PREAMBLE_SAMPLE:-}" != "1" ]]; then
  echo "Set SABLE_REGENERATE_SCHEMA_PREAMBLE_SAMPLE=1 to regenerate the sample artifacts." >&2
  exit 1
fi

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

sample_directory="samples/Sable.Samples.SchemaPreamble"
project_file="$sample_directory/Sable.Samples.SchemaPreamble.csproj"
output_directory="$sample_directory/sable"
temporary_directory=".pi/tmp/schema-preamble-sample-generation"
options_file="$temporary_directory/container-options.json"
backup_directory="$temporary_directory/sable-backup"
had_existing_output=false
generation_complete=false

mkdir -p "$temporary_directory"
rm -rf "$backup_directory"
cleanup() {
  status=$?
  if [[ "$generation_complete" != "true" ]]; then
    rm -rf "$output_directory"
    if [[ "$had_existing_output" == "true" && -d "$backup_directory" ]]; then
      mv "$backup_directory" "$output_directory"
    fi
  else
    rm -rf "$backup_directory"
  fi

  rm -f "$options_file"
  rmdir "$temporary_directory" 2>/dev/null || true
  return "$status"
}
trap cleanup EXIT

port="$(python - <<'PY'
import socket
with socket.socket() as listener:
    listener.bind(("127.0.0.1", 0))
    print(listener.getsockname()[1])
PY
)"
database="sable_sample_generation"
password="$(python - <<'PY'
import secrets
print(secrets.token_hex(24))
PY
)"

cat > "$options_file" <<JSON
{
  "Image": "postgres:15.1",
  "PortBindings": [
    {
      "HostPort": $port,
      "ContainerPort": 5432
    }
  ],
  "EnvironmentVariables": {
    "PGPORT": "5432",
    "POSTGRES_DB": "$database",
    "POSTGRES_USER": "postgres",
    "POSTGRES_PASSWORD": "$password"
  },
  "ConnectionString": "Host=127.0.0.1;Port=$port;Username=postgres;Password=$password;Database=$database;Pooling=false"
}
JSON

unset SABLE_SAMPLE_INCLUDE_PENDING_CHANGE
if [[ -d "$output_directory" ]]; then
  mv "$output_directory" "$backup_directory"
  had_existing_output=true
fi

dotnet run --project src/Sable.Cli/Sable.Cli.csproj -- \
  init \
  --project "$project_file" \
  --database marten://store/ \
  --schema schema_preamble_sample \
  --container-options "$options_file"

if ! find "$output_directory/Marten/migrations" -maxdepth 1 -name '*_Initial.sql' -print -quit | grep -q .; then
  echo "Sable did not generate the required Initial migration." >&2
  exit 1
fi

generation_complete=true
echo "Generated sample artifacts under $output_directory. Review them without editing."
