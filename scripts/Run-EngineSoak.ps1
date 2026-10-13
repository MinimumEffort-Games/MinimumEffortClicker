param([ValidateRange(6,28800)][int]$Seconds = 360, [switch]$Background)
. "$PSScriptRoot/Common.ps1"
$soakReport = Join-Path $ProjectRoot ('artifacts/engine-soak/' + [Guid]::NewGuid().ToString('N'))
$soakBuild = Join-Path $ProjectRoot 'tools/EngineSoak/bin/Release/net10.0'
if (!(Test-Path -LiteralPath (Join-Path $soakBuild 'EngineSoak.dll'))) { throw 'Run scripts/Build.ps1 first.' }
# Freeze the exact binaries so later builds neither lock files nor change the running evidence.
$soakRuntime = $soakReport + '-runtime'
Copy-Item -LiteralPath $soakBuild -Destination $soakRuntime -Recurse
$soakDll = Join-Path $soakRuntime 'EngineSoak.dll'
Get-ChildItem -LiteralPath $soakRuntime -Filter '*.dll' | Get-FileHash -Algorithm SHA256 |
    Select-Object @{Name='File';Expression={Split-Path -Leaf $_.Path}},Hash | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $soakRuntime 'binary-hashes.json')
$soakArgs = @('"' + $soakDll + '"', $Seconds, '"' + $soakReport + '"')
if ($Background) {
    $soakProcess = Start-Process -FilePath $Dotnet -ArgumentList $soakArgs -WindowStyle Hidden -PassThru
    [pscustomobject]@{ Pid = $soakProcess.Id; Seconds = $Seconds; Report = $soakReport } | ConvertTo-Json
} else {
    & $Dotnet $soakDll $Seconds $soakReport
    if ($LASTEXITCODE -ne 0) { throw "Engine soak failed; inspect $soakReport" }
    Get-Content -LiteralPath (Join-Path $soakReport 'result.json')
}
