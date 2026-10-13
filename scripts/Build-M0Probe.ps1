. "$PSScriptRoot/Common.ps1"
Push-Location $ProjectRoot
try {
    # Both executables use the same pinned runtime. Publish side by side so the ZIP
    # carries one Windows Desktop runtime instead of a second copy under target/.
    $bundle = Join-Path $ProjectRoot 'artifacts/m0-probe/bundle-shared-runtime'
    New-Item -ItemType Directory -Force -Path $bundle | Out-Null
    Invoke-Dotnet -Arguments @('publish','tools/WindowsProbe/WindowsProbe.csproj','-c','Release','--self-contained','true','-p:PublishTrimmed=false','-p:RestoreLockedMode=true','-o',"$bundle/probe")
    Invoke-Dotnet -Arguments @('publish','src/MinimumEffortClicker.TestTarget/MinimumEffortClicker.TestTarget.csproj','-c','Release','--self-contained','true','-p:PublishTrimmed=false','-p:RestoreLockedMode=true','-o',"$bundle/probe")
    Copy-Item -LiteralPath "$ProjectRoot/licenses/dotnet/LICENSE.txt","$ProjectRoot/licenses/dotnet/ThirdPartyNotices.txt" -Destination $bundle -Force
    Compress-Archive -Path "$bundle/*" -DestinationPath "$ProjectRoot/artifacts/m0-probe/ProbeBundle.zip" -Force
    (Get-FileHash -LiteralPath "$ProjectRoot/artifacts/m0-probe/ProbeBundle.zip" -Algorithm SHA256).Hash |
        Set-Content -LiteralPath "$ProjectRoot/artifacts/m0-probe/ProbeBundle.sha256"
} finally { Pop-Location }
