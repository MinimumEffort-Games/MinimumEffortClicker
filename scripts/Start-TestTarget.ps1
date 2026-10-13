. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    $project = 'src/MinimumEffortClicker.TestTarget/MinimumEffortClicker.TestTarget.csproj'
    Invoke-Dotnet -Arguments @('restore', $project, '--locked-mode')
    Invoke-Dotnet -Arguments @('run', '--project', $project, '-c', 'Release', '--no-restore')
} finally { Pop-Location }
