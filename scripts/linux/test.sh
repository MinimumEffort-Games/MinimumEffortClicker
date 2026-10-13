#!/usr/bin/env bash
# Builds, then runs the default tests. They use fakes and send no desktop input.
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
CONFIGURATION="${1:-Release}"
bash "$ROOT/scripts/linux/build.sh" "$CONFIGURATION"
"$DOTNET" test "$SOLUTION" --no-build --no-restore -c "$CONFIGURATION" --results-directory "$ROOT/artifacts/test-results" --logger trx
