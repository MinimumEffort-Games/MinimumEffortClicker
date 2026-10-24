# Builds the portable ZIP. Default destination: artifacts/package/MinimumEffortClicker-<version>-win-x64 (git-ignored).
# -Steam builds the Steam package instead (scripts/Build-Steam.ps1): no portable.mode, so data lives in %LocalAppData%
# where Steam Auto-Cloud syncs it; steam.mode switches the Workshop on; steam_api64.dll comes from the Steamworks SDK
# folder given in -SteamworksSdk, because Valve's terms do not allow it in this public repository.
param([string]$Destination, [switch]$Zip, [switch]$Steam, [string]$SteamworksSdk)
. "$PSScriptRoot/Common.ps1"
$appVersion = [string]$buildProperties.Project.PropertyGroup.Version
if (!$Destination) { $Destination = Join-Path $ProjectRoot "artifacts/package/MinimumEffortClicker-$appVersion-$(if ($Steam) { 'steam' } else { 'win-x64' })" }
$steamApi = if ($Steam -and $SteamworksSdk) { Join-Path $SteamworksSdk 'redistributable_bin/win64/steam_api64.dll' }
if ($Steam -and !($steamApi -and (Test-Path -LiteralPath $steamApi))) { throw '-Steam needs -SteamworksSdk pointing at the unpacked Steamworks SDK 1.60 (the folder that holds redistributable_bin).' }
$packagePath = [IO.Path]::GetFullPath($Destination)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $packagePath) | Out-Null
$schemaMatch = Select-String -LiteralPath (Join-Path $ProjectRoot 'src/MinimumEffortClicker.Core/ProfileDefinition.cs') -Pattern 'CurrentVersion = (\d+)'
$profileSchema = [int]$schemaMatch.Matches[0].Groups[1].Value
if (Test-Path -LiteralPath $packagePath) { throw 'Choose a fresh destination; existing data/builds are never overwritten.' }
if (Test-Path -LiteralPath ($packagePath + '.zip')) { throw 'Archive already exists; choose a fresh destination.' }
if (!(Test-Path -LiteralPath (Join-Path $ProjectRoot 'LICENSE'))) { throw 'An approved source licence is required.' }
& "$PSScriptRoot/Test.ps1"
$sourceCommit = (& git -C $ProjectRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'A committed source snapshot is required.' }
if (@(& git -C $ProjectRoot status --porcelain).Count) { throw 'Commit source changes before packaging.' }
New-Item -ItemType Directory -Path $packagePath | Out-Null
Push-Location $ProjectRoot
try {
    foreach ($project in @('MinimumEffortClicker.App') + @(if (!$Steam) { 'MinimumEffortClicker.TestTarget' })) {
        Invoke-Dotnet -Arguments @('publish',"src/$project/$project.csproj",'-c','Release','--self-contained','true',
            '-p:PublishTrimmed=false','-p:RestoreLockedMode=true','-m:1','-nr:false','-p:UseSharedCompilation=false','-o',$packagePath)
    }
    if ($Steam) {
        Set-Content -LiteralPath (Join-Path $packagePath 'steam.mode') -Value 'Steam build: Steam Workshop on; data in %LocalAppData%\MinimumEffortClicker for Steam Cloud.' -Encoding utf8
        Copy-Item -LiteralPath $steamApi -Destination $packagePath
    } else {
        Set-Content -LiteralPath (Join-Path $packagePath 'portable.mode') -Value 'Store local user data in Data beside these executables.' -Encoding utf8
    }
    $licenseDirectory = Join-Path $packagePath 'licenses'
    New-Item -ItemType Directory -Path $licenseDirectory | Out-Null
    foreach ($item in @('dotnet','nuget','native','nuget-inventory.json')) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot "licenses/$item") -Destination $licenseDirectory -Recurse
    }
    foreach ($item in @('LICENSE','THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $ProjectRoot $item) -Destination $packagePath
    }
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'README.md') -Destination (Join-Path $packagePath 'START HERE.md')
    $helpDirectory = Join-Path $packagePath 'docs'
    New-Item -ItemType Directory -Path $helpDirectory | Out-Null
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'docs/RELEASE_STATUS.md') -Destination $helpDirectory
    $forbidden = @(Get-ChildItem -LiteralPath $packagePath -Recurse | Where-Object {
        $_.Name -in @('Data','.git') -or $_.Name -eq 'steam_appid.txt' -or (!$Steam -and $_.Name -in @('steam.mode','steam_api64.dll')) -or $_.Extension -in @('.pfx','.p12','.pem','.key','.ttf') -or $_.FullName -match '[\\/]Pixel[\\/]'
    })
    if ($forbidden.Count) { throw 'Unexpected private data, key or historical font/artwork in package.' }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $packagePath 'MinimumEffortClicker.App.dll')).ProductVersion
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $packagePath 'MinimumEffortClicker.App.runtimeconfig.json') -Raw | ConvertFrom-Json
    $manifest = [ordered]@{
        buildUtc = [DateTimeOffset]::UtcNow; version = $version; schema = $profileSchema
        kind = $(if ($Steam) { 'Steam build' } else { 'Unsigned release' }); sourceCommit = $sourceCommit
        runtime = @($runtimeConfig.runtimeOptions.includedFrameworks | Select-Object name,version)
        portable = !$Steam; steam = [bool]$Steam; signing = 'Unsigned; no publisher Authenticode certificate'
        files = @(Get-ChildItem -LiteralPath $packagePath -File -Recurse | ForEach-Object {
            [ordered]@{path = [IO.Path]::GetRelativePath($packagePath,$_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
        })
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $packagePath 'build-manifest.json') -Encoding utf8
    if ($Zip) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::CreateFromDirectory($packagePath,$packagePath + '.zip',[IO.Compression.CompressionLevel]::Optimal,$true)
        ((Get-FileHash -LiteralPath ($packagePath + '.zip') -Algorithm SHA256).Hash + '  ' + [IO.Path]::GetFileName($packagePath + '.zip')) |
            Set-Content -LiteralPath ($packagePath + '.zip.sha256') -Encoding ascii
        & "$PSScriptRoot/Verify-PersonalZip.ps1" -Archive ($packagePath + '.zip')
    }
    Write-Output $packagePath
} finally { Pop-Location }
