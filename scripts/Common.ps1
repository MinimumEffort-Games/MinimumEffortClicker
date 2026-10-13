$ErrorActionPreference = 'Stop'
$ProjectRoot = Split-Path -Parent $PSScriptRoot
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $ProjectRoot 'Directory.Build.props') -Raw
$WindowsTargetFramework = [string]$buildProperties.Project.PropertyGroup.MecWindowsTargetFramework
$localDotnet = Join-Path $ProjectRoot '.tools/dotnet/dotnet.exe'
$Dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
# Process-local build settings; never changes the user's global configuration.
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if (Test-Path -LiteralPath $localDotnet) { $env:DOTNET_ROOT = Split-Path -Parent $localDotnet }

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & $Dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}
