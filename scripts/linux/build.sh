#!/usr/bin/env bash
# Builds the Linux solution with locked package versions. Usage: build.sh [Debug|Release]
source "$(dirname "${BASH_SOURCE[0]}")/common.sh"
CONFIGURATION="${1:-Release}"
"$DOTNET" restore "$SOLUTION" --locked-mode
"$DOTNET" build "$SOLUTION" --no-restore -c "$CONFIGURATION" -nr:false -p:UseSharedCompilation=false
