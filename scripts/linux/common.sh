# Shared settings for the Linux build scripts. Process-local; never changes global configuration.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 TESTINGPLATFORM_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 AVALONIA_TELEMETRY_OPTOUT=1
if [[ -x "$ROOT/.tools/dotnet/dotnet" ]]; then DOTNET="$ROOT/.tools/dotnet/dotnet"; export DOTNET_ROOT="$ROOT/.tools/dotnet"
elif command -v dotnet >/dev/null; then DOTNET="$(command -v dotnet)"
elif [[ -x "$HOME/.dotnet/dotnet" ]]; then DOTNET="$HOME/.dotnet/dotnet"; export DOTNET_ROOT="$HOME/.dotnet"
else echo "The .NET SDK pinned in global.json is required. See README (Linux)." >&2; exit 1; fi
SOLUTION="$ROOT/MinimumEffortClicker.Linux.slnx"
