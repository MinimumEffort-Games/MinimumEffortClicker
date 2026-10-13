# Re-run after a reviewed dependency update and restore. Uses local package metadata only.
. "$PSScriptRoot/Common.ps1"
$entries = @{}
foreach ($project in Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src'), (Join-Path $ProjectRoot 'tests') -Filter '*.csproj' -Recurse) {
    $assetsPath = Join-Path $project.Directory.FullName 'obj/project.assets.json'
    if (!(Test-Path -LiteralPath $assetsPath)) { throw "Restore first: $assetsPath is missing." }
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        if ($entries.ContainsKey($library.Name)) { continue }
        $packageDir = $null
        foreach ($root in $assets.packageFolders.PSObject.Properties.Name) {
            $candidate = Join-Path $root $library.Value.path
            if (Test-Path -LiteralPath $candidate) { $packageDir = $candidate; break }
        }
        if (!$packageDir) { throw "Package not found: $($library.Name)" }
        $nuspec = Get-ChildItem -LiteralPath $packageDir -Filter '*.nuspec' | Select-Object -First 1
        [xml]$xml = Get-Content -LiteralPath $nuspec.FullName -Raw
        $meta = $xml.package.metadata
        $noticePaths = @()
        foreach ($notice in Get-ChildItem -LiteralPath $packageDir -File | Where-Object Name -Match '^(license|licence|third.?party.?notices|notice)(\.|$)') {
            $relative = "licenses/nuget/$($meta.id)/$($meta.version)/$($notice.Name)"
            $destination = Join-Path $ProjectRoot $relative
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $notice.FullName -Destination $destination -Force
            $noticePaths += $relative
        }
        $entries[$library.Name] = [ordered]@{
            package = [string]$meta.id
            version = [string]$meta.version
            licenseType = [string]$meta.license.type
            license = [string]$meta.license.'#text'
            licenseUrl = [string]$meta.licenseUrl
            repository = [string]$meta.repository.url
            commit = [string]$meta.repository.commit
            contentHash = [string]$library.Value.sha512
            localNotices = $noticePaths
        }
    }
}
# Windows SDK projections are implicit framework references, absent from assets.libraries.
$sdkVersion = [string]$buildProperties.Project.PropertyGroup.WindowsSdkPackageVersion
$sdkRoot = $null
foreach ($root in $assets.packageFolders.PSObject.Properties.Name) {
    $candidate = Join-Path $root "microsoft.windows.sdk.net.ref/$sdkVersion"
    if (Test-Path -LiteralPath $candidate) { $sdkRoot = $candidate; break }
}
if (!$sdkRoot) { throw "Pinned Windows SDK projection package $sdkVersion was not restored." }
[xml]$sdkMetadata = Get-Content -LiteralPath (Join-Path $sdkRoot 'microsoft.windows.sdk.net.ref.nuspec') -Raw
$entries["Microsoft.Windows.SDK.NET.Ref/$sdkVersion"] = [ordered]@{
    package = 'Microsoft.Windows.SDK.NET.Ref'; version = $sdkVersion; licenseType = 'url'
    license = 'Microsoft Windows SDK licence'; licenseUrl = [string]$sdkMetadata.package.metadata.licenseUrl
    contentHash = (Get-Content -LiteralPath (Join-Path $sdkRoot "microsoft.windows.sdk.net.ref.$sdkVersion.nupkg.sha512") -Raw).Trim()
    localNotices = @(); implicitFrameworkReference = $true
}
$inventory = @($entries.Keys | Sort-Object | ForEach-Object { $entries[$_] })
$inventory | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $ProjectRoot 'licenses/nuget-inventory.json') -Encoding utf8
Write-Host "Recorded $($inventory.Count) package licences and copied available notices."
