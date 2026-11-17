#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
root=$PWD
export BOZZETTO_AGENT=codex-sqlite
export BOZZETTO_PROJECT="$root"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
if [[ -x /home/hhh/.local/share/mise/installs/dotnet/10/dotnet ]]; then
    export PATH="/home/hhh/.local/share/mise/installs/dotnet/10:$PATH"
fi
export TMPDIR="$root/test-results/scratch/tmp"
mkdir -p "$TMPDIR" "$root/test-results"
lease=(dotnet fsi "$root/../Bozzetto/tools/build/lease.fsx")
cleanup() { rm -rf "$root/test-results/scratch"; }
trap cleanup EXIT
mkdir -p test-results/scratch/toolchain
cp -a ../Composer/src/bin/Debug/net10.0 test-results/scratch/toolchain/composer
cp -a ../Farscape/src/Farscape.Cli/bin/Debug/net10.0 test-results/scratch/toolchain/farscape
"${lease[@]}" full_build dotnet build tests/Fidelity.SQLite.Tests/Fidelity.SQLite.Tests.fsproj --no-restore -p:BuildProjectReferences=false -p:ComposerBin="$root/test-results/scratch/toolchain/composer" -p:FarscapeBin="$root/test-results/scratch/toolchain/farscape" -v:minimal 2>&1 | tee test-results/build.log
python3 - <<'PY'
from pathlib import Path
import hashlib, json, subprocess, tomllib
root=Path.cwd()
paths=list((root/'test-results/scratch/toolchain').rglob('*.dll'))+list((root/'tests/Fidelity.SQLite.Tests/bin/Debug/net10.0').glob('*.dll'))
manifest={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
for name in ['Composer','Clef.Compiler.Service','Fidelity.PSG','Alex','BAREWire','Fidelity.Data','XParsec','Fidelity.FSharp.Incremental','Fidelity.FSharp.Incremental.Hosting']:
    if manifest['test-results/scratch/toolchain/composer/'+name+'.dll'] != manifest['tests/Fidelity.SQLite.Tests/bin/Debug/net10.0/'+name+'.dll']:
        raise SystemExit('Shared assembly mismatch: '+name)
heads={name:subprocess.check_output(['git','-C',str(root.parent/name),'rev-parse','HEAD'],text=True).strip() for name in ['Fidelity.SQLite','Farscape','Composer','clef','Fidelity.PSG','Alex','BAREWire','Fidelity.Platform']}
inputs={}
def bind(path):
    path=path.resolve()
    if str(path) in inputs: return
    content=path.read_bytes()
    inputs[str(path)]=hashlib.sha256(content).hexdigest()
    if path.suffix != '.fidproj': return
    project=tomllib.loads(content.decode())
    for source in project.get('build',{}).get('sources',[]):
        bind(path.parent/source)
    for dep in project.get('dependencies',{}).values():
        if isinstance(dep,dict) and 'path' in dep:
            bind(path.parent/dep['path'])
bind(root/'Fidelity.SQLite.fidproj')
bind(root.parent/'Fidelity.Platform/Environments/Linux/x86_64/Fidelity.Libc.fidproj')
for name in ['pilot/sqlite.pilot.toml','tests/Fidelity.SQLite.Tests/Suite.fs','tests/Fidelity.SQLite.Tests/Positive.clef','tests/Fidelity.SQLite.Tests/probe.c','tests/Fidelity.SQLite.Tests/Fidelity.SQLite.Tests.fsproj']:
    bind(root/name)
(root/'test-results/dependencies.json').write_text(json.dumps({'heads':heads,'assemblySha256':manifest,'sourceSha256':inputs},indent=2)+'\n')
PY
set +e
rm -f test-results/acceptance.trx
"${lease[@]}" test_suite_run dotnet vstest tests/Fidelity.SQLite.Tests/bin/Debug/net10.0/Fidelity.SQLite.Tests.dll '--logger:trx;LogFileName=acceptance.trx' --ResultsDirectory:"$root/test-results" "$@" 2>&1 | tee test-results/tests.log
status=${PIPESTATUS[0]}
set -e
python3 - <<'PYEND'
from pathlib import Path
import hashlib,json,xml.etree.ElementTree as ET
root=Path.cwd()
m=json.loads((root/'test-results/dependencies.json').read_text())
changed=[p for p,h in m['sourceSha256'].items() if not Path(p).is_file() or hashlib.sha256(Path(p).read_bytes()).hexdigest()!=h]
(root/'test-results/input-stability.json').write_text(json.dumps({'unchanged':not changed,'changed':changed},indent=2)+'\n')
if changed: raise SystemExit('Gate input changed during run: '+', '.join(changed))
if not (root/'test-results/acceptance.trx').is_file(): raise SystemExit('No test result was produced; no suite verdict')
report=ET.parse(root/'test-results/acceptance.trx')
n={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
c=report.find('.//t:Counters',n).attrib
print('SQLite suite: total={total} executed={executed} passed={passed} failed={failed} notExecuted={notExecuted}'.format(**c))
if report.find('.//t:ResultSummary',n).attrib['outcome']=='Aborted': raise SystemExit('Test run was aborted; no full-suite verdict')
if int(c['total'])==0 or c['executed']!=c['total']: raise SystemExit('Suite execution count mismatch')
PYEND
exit "$status"
