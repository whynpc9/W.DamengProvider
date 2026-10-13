#!/usr/bin/env bash
set -euo pipefail
set +x

# Retired before argument handling, file access, builds, secrets or database work.
printf '%s\n' '{"schema_version":1,"task":"T17-profile","status":"retired","classification":"legacy_profile_retired","legacy_package_version":"0.1.0-r3.t16.20261002143244","documentation":"eng/T17-profile.md","exit_code":69}'
exit 69
