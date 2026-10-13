param(
    [string]$Executable,
    [Parameter(Mandatory)][string]$Report,
    [ValidateRange(60,28800)][int]$Seconds = 28800,
    [switch]$Observe,
    [int]$ProductProcessId
)
$ErrorActionPreference = 'Stop'
$reportPath = [IO.Path]::GetFullPath($Report)
if (!$Observe) {
    if (Get-Process -Name MinimumEffortClicker.App -ErrorAction SilentlyContinue) { throw 'Exit the clicker before the isolated endurance launch.' }
    $exePath = [IO.Path]::GetFullPath($Executable)
    if (!(Test-Path -LiteralPath $exePath) -or (Test-Path -LiteralPath $reportPath) -or (Test-Path -LiteralPath ($reportPath+'.progress.json'))) { throw 'Choose an existing isolated build and fresh report path.' }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath)) | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new($exePath)
    $start.UseShellExecute = $false; $start.WorkingDirectory = [IO.Path]::GetDirectoryName($exePath)
    foreach ($argument in @('--endurance-report',$reportPath,[string]$Seconds)) { $start.ArgumentList.Add($argument) }
    $product = [Diagnostics.Process]::Start($start)
    $observerStart = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    $observerStart.UseShellExecute = $false; $observerStart.CreateNoWindow = $true
    foreach ($argument in @('-NoProfile','-File',$PSCommandPath,'-Observe','-Report',$reportPath,'-ProductProcessId',[string]$product.Id,'-Seconds',[string]$Seconds)) { $observerStart.ArgumentList.Add($argument) }
    $observer = [Diagnostics.Process]::Start($observerStart)
    [ordered]@{ productPid=$product.Id; observerPid=$observer.Id; report=$reportPath; requestedSeconds=$Seconds;
        start='Awaiting explicit UI Start; no macro is loaded or started by this script.' } | ConvertTo-Json
    $product.Dispose(); $observer.Dispose(); return
}

# Read-only external resource sampling. No input, hooks, UI commands or process termination.
$product = Get-Process -Id $ProductProcessId
if ($product.ProcessName -ne 'MinimumEffortClicker.App') { throw 'Only the product process can be observed.' }
$watch = [Diagnostics.Stopwatch]::StartNew(); $previousSeconds=0d
$product.Refresh(); $previousCpu=$product.TotalProcessorTime
$maximumPrivateBytes=0L; $maximumHandles=0; $maximumCpu=0d; $samples=0
$violations=[Collections.Generic.HashSet[string]]::new()
while ($watch.Elapsed.TotalSeconds -lt $Seconds + 900) {
    $product.Refresh()
    if ($product.HasExited) { break }
    $elapsed=$watch.Elapsed.TotalSeconds; $cpu=$product.TotalProcessorTime
    $cpuPercent=if ($elapsed-$previousSeconds -gt 1) {100*($cpu-$previousCpu).TotalSeconds/($elapsed-$previousSeconds)} else {0}
    $maximumPrivateBytes=[Math]::Max($maximumPrivateBytes,$product.PrivateMemorySize64)
    $maximumHandles=[Math]::Max($maximumHandles,$product.HandleCount)
    $maximumCpu=[Math]::Max($maximumCpu,$cpuPercent)
    if ($product.PrivateMemorySize64 -gt 256MB) { [void]$violations.Add('Private memory exceeded 256 MiB.') }
    if ($product.HandleCount -gt 2000) { [void]$violations.Add('Handle count exceeded 2000.') }
    if (!$product.Responding) { [void]$violations.Add('Main window did not respond to the OS health query.') }
    [ordered]@{utc=[DateTimeOffset]::UtcNow; elapsedSeconds=$elapsed; privateBytes=$product.PrivateMemorySize64;
        handles=$product.HandleCount; cpuPercentOneCore=$cpuPercent; responding=$product.Responding} |
        ConvertTo-Json -Compress | Add-Content -LiteralPath ($reportPath+'.resources.jsonl') -Encoding utf8
    $samples++; $previousSeconds=$elapsed; $previousCpu=$cpu
    if (Test-Path -LiteralPath $reportPath) { break }
    Start-Sleep -Seconds 30
}
$complete=Test-Path -LiteralPath $reportPath
if (!$complete) { [void]$violations.Add('Process exited or observation timed out without an endurance result.') }
[ordered]@{scope='External resource observations of the opt-in full WPF input-free endurance run'; utc=[DateTimeOffset]::UtcNow;
    samples=$samples; maximumPrivateBytes=$maximumPrivateBytes; maximumHandles=$maximumHandles; maximumCpuPercentOneCore=$maximumCpu;
    violations=@($violations); resultAvailable=$complete; passedResourceBounds=$complete -and $violations.Count -eq 0} |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($reportPath+'.resources-result.json') -Encoding utf8
$product.Dispose()
