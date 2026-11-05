#!/usr/bin/env bash
set -euo pipefail
umask 077
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"
mkdir -p .pi/tmp
temporary="$(mktemp -d "$repo_root/.pi/tmp/catalog-package.XXXXXX")"
cleanup() {
  status=$?
  trap - EXIT
  rm -rf "$temporary"
  exit "$status"
}
trap cleanup EXIT

# Build/Pack are prerequisites, not publication. Every command here is bounded;
# the integration scripts below own their process-tree deadlines and cleanup.
python - "$temporary" <<'PY'
from pathlib import Path
import hashlib, json, os, re, shutil, signal, subprocess, sys, xml.etree.ElementTree as ET, zipfile
root, temporary = Path.cwd(), Path(sys.argv[1])
revision = subprocess.run(['git', 'rev-parse', 'HEAD'], capture_output=True, text=True,
                          check=True, timeout=20).stdout.strip()
packages = {}
for file in (root / 'Artifacts').glob('*.nupkg'):
    if file.name.endswith('.symbols.nupkg'):
        continue
    with zipfile.ZipFile(file) as archive:
        specification = [name for name in archive.namelist() if name.endswith('.nuspec')]
        if len(specification) != 1:
            raise RuntimeError('Expected one package manifest.')
        metadata = ET.fromstring(archive.read(specification[0])).find('{*}metadata')
        identity, version = metadata.findtext('{*}id'), metadata.findtext('{*}version')
        if identity.lower() not in ('sable.cli', 'sable'):
            continue
        if identity.lower() in packages:
            raise RuntimeError('Multiple local package versions found; rerun Cake Build and Pack.')
        repository = metadata.find('{*}repository')
        if repository is None or repository.get('commit') != revision:
            raise RuntimeError('Local package does not identify current source revision; rerun Build then Pack.')
        if identity.lower() == 'sable.cli' and not any(
            element.get('name', '').lower() == 'dotnettool'
            for element in metadata.findall('.//{*}packageType')
        ):
            raise RuntimeError('CLI package is not a dotnet tool.')
        if identity.lower() == 'sable.cli':
            assemblies = [name for name in archive.namelist() if name.endswith('/Sable.Cli.dll')]
            if len(assemblies) != 1:
                raise RuntimeError('Expected one packaged CLI assembly.')
            assembly = archive.read(assemblies[0])
            built = root / 'src' / 'Sable.Cli' / 'bin' / 'Release' / 'net10.0' / 'Sable.Cli.dll'
            if assembly != built.read_bytes() or (version + '+' + revision).encode('utf-8') not in assembly:
                raise RuntimeError('Packaged CLI binary does not match the current revision-stamped Release build.')
            cli_assembly_sha = hashlib.sha256(assembly).hexdigest()
        packages[identity.lower()] = (file, version)
if set(packages) != {'sable.cli', 'sable'}:
    raise RuntimeError('Expected locally packed Sable.Cli and Sable packages in Artifacts.')
version = packages['sable.cli'][1]
if packages['sable'][1] != version:
    raise RuntimeError('Local library and CLI package versions differ.')
feed, tools = temporary / 'feed', temporary / 'tools'
feed.mkdir()
for file, _ in packages.values():
    shutil.copy2(file, feed / file.name)
configuration = ET.Element('configuration')
sources = ET.SubElement(configuration, 'packageSources')
ET.SubElement(sources, 'clear')
ET.SubElement(sources, 'add', key='local', value=str(feed.resolve()))
ET.SubElement(sources, 'add', key='nuget.org', value='https://api.nuget.org/v3/index.json')
mapping = ET.SubElement(configuration, 'packageSourceMapping')
for key, patterns in [('local', ['Sable.Cli', 'Sable']), ('nuget.org', ['*'])]:
    source = ET.SubElement(mapping, 'packageSource', key=key)
    for pattern in patterns:
        ET.SubElement(source, 'package', pattern=pattern)
config = temporary / 'NuGet.Config'
ET.ElementTree(configuration).write(config, encoding='utf-8', xml_declaration=True)
environment = os.environ.copy()
environment.update({'DOTNET_CLI_HOME': str(temporary / 'home'),
                    'NUGET_PACKAGES': str(temporary / 'packages'),
                    'NUGET_HTTP_CACHE_PATH': str(temporary / 'http-cache'),
                    'DOTNET_NOLOGO': 'true', 'DOTNET_CLI_TELEMETRY_OPTOUT': 'true',
                    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE': 'true'})

def run(arguments, expected=0, timeout=60):
    process = subprocess.Popen([str(argument) for argument in arguments],
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                               env=environment, start_new_session=os.name != 'nt')
    try:
        output, _ = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired:
        if os.name == 'nt':
            subprocess.run(['taskkill', '/PID', str(process.pid), '/T', '/F'],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=20)
        else:
            os.killpg(process.pid, signal.SIGKILL)
        process.communicate(timeout=20)
        raise RuntimeError('Isolated package verification command timed out.')
    text = output.decode('utf-8', errors='replace')
    if process.returncode != expected:
        print(text)
        raise RuntimeError('Isolated package command returned unexpected exit status.')
    return text

run(['dotnet', 'tool', 'install', 'Sable.Cli', '--version', version, '--tool-path', tools,
     '--configfile', config, '--no-cache'], timeout=240)
listing = run(['dotnet', 'tool', 'list', '--tool-path', tools])
if 'sable.cli' not in listing.lower() or version not in listing:
    raise RuntimeError('Installed isolated tool identity/version mismatch.')
executable = (tools / ('sable.exe' if os.name == 'nt' else 'sable')).resolve()
if not executable.is_file():
    raise RuntimeError('Expected isolated executable is absent; never fall back to global sable.')
if 'migrations' not in run([executable, '--help']):
    raise RuntimeError('Packaged tool help did not expose existing migration commands.')
project_directory = temporary / 'fixture'
migrations = project_directory / 'sable' / 'Marten' / 'migrations'
migrations.mkdir(parents=True)
project = project_directory / 'Fixture.csproj'
project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>', encoding='utf-8')
source = root / 'samples' / 'Sable.Samples.CatalogGuardedSchemaPreamble' / 'sable' / 'Marten'
shutil.copy2(source / 'schema.txt', migrations.parent / 'schema.txt')
for file in (source / 'migrations').glob('*.sql'):
    shutil.copy2(file, migrations / file.name)
initial = next((source / 'migrations').glob('*_Initial.sql')).read_bytes()
preamble = re.search(rb'\bDO\s+\$\$.*?\$\$;', initial, flags=re.DOTALL).group(0)
output = temporary / 'script.sql'
arguments = ['--project', project, '--database', 'marten://store/', '--output', output]
run([executable, 'migrations', 'script', *arguments])
if preamble not in output.read_bytes():
    raise RuntimeError('Packaged CLI did not preserve the authentic preamble bytes.')
run([executable, 'migrations', 'backfill', *arguments])
backfill = output.read_text(encoding='utf-8-sig')
if 'pg_namespace' in backfill or 'INSERT INTO' not in backfill:
    raise RuntimeError('Packaged backfill changed existing history-only semantics.')
# Handwritten adversarial fixture; never edit an authentic generated artifact.
(migrations / '20990101000000_Rejected.sql').write_text('DO $$ BEGIN RAISE NOTICE \'unsupported\'; END $$;', encoding='utf-8')
original = b'preserve-existing-output\r\n\x00'
output.write_bytes(original)
rejection = run([executable, 'migrations', 'script', *arguments], expected=1)
if "Sable cannot safely wrap migration '20990101000000_Rejected'" not in rejection or output.read_bytes() != original:
    raise RuntimeError('Packaged rejection diagnostic/output preservation contract failed.')
if 'Successfully saved' in rejection:
    raise RuntimeError('Rejected package input reported success.')
(temporary / 'executable.txt').write_text(str(executable), encoding='utf-8')
package = packages['sable.cli'][0]
print(json.dumps({'package': package.name, 'version': version, 'sourceRevision': revision,
                  'sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
                  'assemblySha256': cli_assembly_sha,
                  'identity': 'isolated local feed/tool directory/cache; no global replacement'}, indent=2))
print('PASS: installed package help, authentic script/backfill, exact rejection exit and output preservation.')
PY

tool="$(python - "$temporary/executable.txt" <<'PY'
from pathlib import Path
import sys
print(Path(sys.argv[1]).read_text(encoding='utf-8'))
PY
)"
SABLE_TEST_CLI_PATH="$tool" bash verify/catalog-guarded-schema-preambles/postgres.sh
SABLE_TEST_CLI_PATH="$tool" bash verify/catalog-guarded-schema-preambles/e2e.sh
echo "PASS: exact isolated packaged CLI passed disposable privilege/replay and full generation/application workflows."
