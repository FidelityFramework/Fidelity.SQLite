#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export FIDELITY_PLATFORM_PROJECT="$PWD/../Fidelity.Platform/Profiles/Linux_x86_64_Default/Fidelity.Platform.fidproj"
export BAREWIRE_METADATA_PROJECT="$PWD/../BAREWire/src/BAREWire.BindingMetadata.fidproj"
if [[ -x /home/hhh/.local/share/mise/installs/dotnet/10/dotnet ]]; then
    export PATH="/home/hhh/.local/share/mise/installs/dotnet/10:$PATH"
fi
dotnet ../Farscape/src/Farscape.Cli/bin/Debug/net10.0/Farscape.Cli.dll project --project pilot/sqlite.pilot.toml
# The package owns loading selection; Farscape owns the generated ABI surface.
sed -i '/^sources = \[/a\    "Loading.clef",' Fidelity.SQLite.fidproj
sed -i '/^\[platform\]/a\loading = "Fidelity.SQLite.Loading.sqliteProcessLink"' Fidelity.SQLite.fidproj
