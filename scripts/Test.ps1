param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
. "$PSScriptRoot/Common.ps1"
& "$PSScriptRoot/Build.ps1" -Configuration $Configuration
Push-Location $ProjectRoot
try {
    Invoke-Dotnet -Arguments @('test', 'MinimumEffortClicker.slnx', '--no-build', '--no-restore', '-c', $Configuration,
        '--results-directory', 'artifacts/test-results', '--logger', 'trx')
} finally { Pop-Location }
