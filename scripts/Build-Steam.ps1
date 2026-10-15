# Builds the Steam package (no ZIP; SteamPipe uploads the folder). Needs the unpacked Steamworks SDK 1.60, which matches
# Steamworks.NET 2024.8.0; the SDK is downloaded from partner.steamgames.com and never committed.
# Example: scripts/Build-Steam.ps1 -SteamworksSdk D:\SDKs\steamworks_sdk_160\sdk
param([Parameter(Mandatory)][string]$SteamworksSdk, [string]$Destination)
& "$PSScriptRoot/Build-Public.ps1" -Steam -SteamworksSdk $SteamworksSdk -Destination $Destination
