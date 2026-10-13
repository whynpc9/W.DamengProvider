#!/usr/bin/env bash
set -euo pipefail
set +x
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 -B "$repo/tools/NuGetRelease/release.py" "$@"
