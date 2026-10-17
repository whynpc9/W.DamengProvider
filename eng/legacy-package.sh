#!/usr/bin/env bash
# Verify the restricted asset from the package produced by release.sh, on Core 3.1.
set -euo pipefail
set +x
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"
output="${1:-.local/nuget-release}"
platform="${2:-linux/amd64}"
image="${LEGACY_RUNTIME_IMAGE:-mcr.microsoft.com/dotnet/runtime:3.1.32-bullseye-slim}"
output="$(cd "$output" && pwd)"
package="$(cat "$output/package-path.txt")"
version="$(python3 -c 'import json,pathlib,sys; print(json.loads(pathlib.Path(pathlib.Path(sys.argv[1]).read_text().strip()).read_text())["version"])' "$output/manifest-path.txt")"
run="$(mktemp -d "$output/legacy-XXXXXX")"
export DOTNET_CLI_HOME="$run/cli-home" DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false NUGET_PACKAGES="$run/packages"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
cp tools/LegacyCompatibilityProbe/{LegacyCompatibilityProbe.csproj,Program.cs} "$run/"
python3 - "$(dirname "$package")" "$run/NuGet.Config" <<'PY'
import sys,xml.etree.ElementTree as ET
root=ET.Element('configuration')
sources=ET.SubElement(root,'packageSources');ET.SubElement(sources,'clear')
ET.SubElement(sources,'add',key='candidate',value=sys.argv[1])
ET.SubElement(sources,'add',key='public',value='https://api.nuget.org/v3/index.json')
mapping=ET.SubElement(root,'packageSourceMapping');ET.SubElement(mapping,'clear')
local=ET.SubElement(mapping,'packageSource',key='candidate');ET.SubElement(local,'package',pattern='W.DamengProvider')
public=ET.SubElement(mapping,'packageSource',key='public');ET.SubElement(public,'package',pattern='*')
ET.ElementTree(root).write(sys.argv[2],encoding='utf-8',xml_declaration=True)
PY
flags=(-m:1 /nodeReuse:false /p:UseSharedCompilation=false --disable-build-servers)
project="$run/LegacyCompatibilityProbe.csproj"
"$dotnet_command" restore "$project" --configfile "$run/NuGet.Config" -p:UsePackage=true -p:PackageVersion="$version" "${flags[@]}" 2>&1 | tee "$run/restore.log"
"$dotnet_command" build "$project" --no-restore -c Release -o "$run/app" -p:UsePackage=true -p:PackageVersion="$version" "${flags[@]}" 2>&1 | tee "$run/build.log"
python3 - "$package" "$run" "$version" <<'PY'
import json,sys,zipfile
from pathlib import Path
package,run,version=Path(sys.argv[1]),Path(sys.argv[2]),sys.argv[3]
assets=json.loads((run/'obj/project.assets.json').read_text())
product='W.DamengProvider/'+version
if assets['libraries'][product]['type']!='package': raise SystemExit('Legacy consumer requires PackageReference')
selected=assets['targets']['netcoreapp3.1'][product]
expected='lib/netstandard2.1/W.DamengProvider.dll'
if set(selected['compile'])!={expected} or set(selected['runtime'])!={expected}: raise SystemExit('Legacy consumer selected the wrong asset')
with zipfile.ZipFile(package) as archive:
    if (run/'app/W.DamengProvider.dll').read_bytes()!=archive.read(expected): raise SystemExit('Legacy loaded DLL differs from candidate package')
print('Legacy consumer selected the candidate package and netstandard2.1 DLL.')
PY
docker run --rm --platform "$platform" --mount "type=bind,source=$run/app,target=/app,readonly" --workdir /app "$image" dotnet W.DmProvider.LegacyCompatibilityProbe.dll 2>&1 | tee "$run/runtime.log"
printf '%s\n' "$run" > "$output/legacy-path-${platform##*/}.txt"
printf 'Legacy package verified on %s; results: %s\n' "$platform" "$run"
