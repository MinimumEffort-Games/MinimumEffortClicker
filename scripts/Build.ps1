param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [ValidateRange(1, 4)][int]$MaxCpuCount = 1)
. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    # Keep development builds bounded in the 8 GB VM; do not leave compiler workers resident.
    Invoke-Dotnet -Arguments @('restore', 'MinimumEffortClicker.slnx', '--locked-mode', '--disable-parallel', '-m:1', '-nr:false')
    Invoke-Dotnet -Arguments @('build', 'MinimumEffortClicker.slnx', '--no-restore', '-c', $Configuration,
        "-m:$MaxCpuCount", '-nr:false', '-p:UseSharedCompilation=false')
} finally { Pop-Location }
