# Optional local SDK installation. Requires network access; does not need administrator rights.
. "$PSScriptRoot/Common.ps1"
$version = (Get-Content -LiteralPath (Join-Path $ProjectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk = $metadata.releases.sdks | Where-Object version -eq $version | Select-Object -First 1
$asset = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (!$asset -or !$asset.hash) { throw "Official release metadata missing for $version" }
$toolDir = Join-Path $ProjectRoot '.tools'
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
$archive = Join-Path $toolDir "dotnet-sdk-$version-win-x64.zip"
Invoke-WebRequest -Uri $asset.url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $asset.hash) { throw 'SDK SHA-512 mismatch; not extracting.' }
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $toolDir 'dotnet') -Force
Write-Host "Verified SDK $version installed locally in .tools/dotnet."
