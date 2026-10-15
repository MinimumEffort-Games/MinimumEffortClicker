# Uploads a Steam package folder (from scripts/Build-Steam.ps1) with SteamPipe. It never sets a build live: choose the
# build on partner.steamgames.com (SteamPipe > Builds) yourself. SteamCMD asks for the password and Steam Guard code
# itself; nothing here stores them. The depot id is on the Steamworks site under SteamPipe > Depots.
param(
    [Parameter(Mandatory)][string]$SteamCmd,
    [Parameter(Mandatory)][string]$Account,
    [Parameter(Mandatory)][string]$Build,
    [Parameter(Mandatory)][uint32]$DepotId,
    [string]$Description = 'Minimum Effort Clicker build'
)
. "$PSScriptRoot/Common.ps1"
$appId = 5440990
$buildPath = [IO.Path]::GetFullPath($Build)
if (!(Test-Path -LiteralPath (Join-Path $buildPath 'steam.mode'))) { throw 'That folder is not a Steam package; build it with scripts/Build-Steam.ps1.' }
if (Test-Path -LiteralPath (Join-Path $buildPath 'steam_appid.txt')) { throw 'Remove steam_appid.txt: it is for local testing only and must not ship.' }
$work = Join-Path $ProjectRoot 'artifacts/steam'
New-Item -ItemType Directory -Force -Path (Join-Path $work 'output') | Out-Null
$vdf = Join-Path $work "app_build_$appId.vdf"
$quote = { param($text) '"' + ($text -replace '\', '\' -replace '"', '\"') + '"' }
@"
"AppBuild"
{
    "AppID" "$appId"
    "Desc" $(& $quote $Description)
    "ContentRoot" $(& $quote $buildPath)
    "BuildOutput" $(& $quote (Join-Path $work 'output'))
    "Depots"
    {
        "$DepotId"
        {
            "FileMapping" { "LocalPath" "*" "DepotPath" "." "Recursive" "1" }
            "FileExclusion" "*.pdb"
        }
    }
}
"@ | Set-Content -LiteralPath $vdf -Encoding utf8
& $SteamCmd +login $Account +run_app_build $vdf +quit
if ($LASTEXITCODE -ne 0) { throw "SteamCMD failed with exit code $LASTEXITCODE." }
Write-Output 'Uploaded. Set it live on partner.steamgames.com > SteamPipe > Builds when you are ready.'
