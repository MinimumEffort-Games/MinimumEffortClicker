# Opt-in only. Sends bounded input exclusively to the probe's own TestTarget inside an isolated virtual machine.
# Run it as a standard user in the guest with MEC_ISOLATED_VM=1 set; never on a real desktop or in CI.
param([switch]$IsolatedVm, [switch]$Helpers, [switch]$Recovery, [switch]$Performance)
. "$PSScriptRoot/Common.ps1"
if (!$IsolatedVm) { throw 'Explicit -IsolatedVm opt-in is required. F9 stops the bounded probe.' }
if (([int]$Helpers.IsPresent + [int]$Recovery.IsPresent + [int]$Performance.IsPresent) -gt 1) { throw 'Choose one probe mode.' }
if ($env:MEC_ISOLATED_VM -ne '1') { throw 'Set MEC_ISOLATED_VM=1 inside the isolated virtual machine before running the probe.' }
$system = Get-CimInstance Win32_ComputerSystem
if ($system.Manufacturer -notmatch 'VMware|VirtualBox|innotek|QEMU|Parallels|Xen' -and $system.Model -notmatch 'Virtual') { throw 'Probe requires an isolated virtual machine guest.' }
$identity = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($identity.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run as a standard user.' }
$probe = Join-Path $ProjectRoot 'artifacts/m0-probe/bundle-shared-runtime/probe/MinimumEffortClicker.WindowsProbe.exe'
if (!(Test-Path -LiteralPath $probe)) { throw 'Run scripts/Build-M0Probe.ps1 first.' }
$runId = [Guid]::NewGuid().ToString('N')
$directory = Join-Path $ProjectRoot "artifacts/m0-local/$runId"
New-Item -ItemType Directory -Path $directory | Out-Null
$environment = [ordered]@{
    RunId = $runId
    StartedAtUtc = [DateTime]::UtcNow.ToString('o')
    Cpu = Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors
    OS = Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, BuildNumber
    GPU = @(Get-CimInstance Win32_VideoController | Select-Object Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate)
    RAMBytes = $system.TotalPhysicalMemory
    Manufacturer = $system.Manufacturer
    PowerPlan = (powercfg /getactivescheme | Out-String).Trim()
    ProbeSha256 = (Get-FileHash -LiteralPath $probe -Algorithm SHA256).Hash
    ProbeAssemblySha256 = (Get-FileHash -LiteralPath ([IO.Path]::ChangeExtension($probe, '.dll')) -Algorithm SHA256).Hash
}
$environment | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $directory 'environment.json')
Write-Host "Bounded VM input test: F9 stops input. Reports: $directory"
$arguments = '--isolated-vm "' + $directory + '"'
if ($Helpers) { $arguments += ' --m1' }
if ($Recovery) { $arguments += ' --recovery' }
if ($Performance) { $arguments += ' --performance' }
$process = Start-Process -FilePath $probe -ArgumentList $arguments -WindowStyle Normal -PassThru
try {
    if (!$process.WaitForExit(105000)) { throw 'Probe exceeded outer timeout. No process was forcibly terminated; use F9 to stop.' }
    $reportPath = Join-Path $directory 'probe-result.json'
    if (!(Test-Path -LiteralPath $reportPath)) { throw "Probe exited with code $($process.ExitCode) without a report. Check VM/user/token and concurrent-probe guards." }
    $reportJson = Get-Content -LiteralPath $reportPath -Raw
    $report = $reportJson | ConvertFrom-Json
    Write-Output $reportJson
    if ($process.ExitCode -ne 0 -or !$report.Passed) { throw "M0 probe failed at '$($report.Stage)': $($report.Error)" }
} finally { $process.Dispose() }
