param(
    [string]$Version = 'preview',
    [string]$NotesFile,
    [ValidateSet('Both', 'GitHub', 'Steam')][string]$Target = 'Both',
    [string]$SteamRoot = 'D:\SteamCMD',
    [switch]$UpdateExistingRelease,
    [switch]$PreserveSteamTags,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
foreach ($name in 'STEAM_USERNAME', 'STEAM_PASSWORD', 'STEAM_REFRESH_TOKEN') {
    if (-not [Environment]::GetEnvironmentVariable($name, 'Process')) {
        $value = [Environment]::GetEnvironmentVariable($name, 'User')
        if (-not $value) { $value = [Environment]::GetEnvironmentVariable($name, 'Machine') }
        if ($value) { [Environment]::SetEnvironmentVariable($name, $value, 'Process') }
    }
}
$releaseArgs = @((Join-Path $PSScriptRoot 'publish-dual.mjs'), '--steam-root', $SteamRoot, '--version', $Version, '--target', $Target)
if ($NotesFile) { $releaseArgs += @('--notes-file', (Resolve-Path -LiteralPath $NotesFile).Path) }
if ($Publish) { $releaseArgs += '--publish' }
if ($UpdateExistingRelease) { $releaseArgs += '--update-existing-release' }
if ($PreserveSteamTags) { $releaseArgs += '--preserve-steam-tags' }
& node @releaseArgs
if ($LASTEXITCODE -ne 0) { throw "发布流程退出码：$LASTEXITCODE" }
