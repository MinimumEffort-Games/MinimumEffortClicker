# Interactive local launch. The application always starts stopped, with hotkey playback disabled.
. "$PSScriptRoot/Common.ps1"
$path = Join-Path $ProjectRoot "src/MinimumEffortClicker.App/bin/Release/$WindowsTargetFramework/win-x64/MinimumEffortClicker.App.exe"
if (!(Test-Path -LiteralPath $path)) { throw 'Run scripts/Build.ps1 first.' }
$process = Start-Process -FilePath $path -WindowStyle Normal -PassThru
try {
    [void]$process.WaitForInputIdle(10000)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "App exited during startup: $($process.ExitCode)." }
        if ($process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 50
    } while ($watch.Elapsed.TotalSeconds -lt 5)
    $process | Select-Object Id, MainWindowTitle
} finally { $process.Dispose() }
