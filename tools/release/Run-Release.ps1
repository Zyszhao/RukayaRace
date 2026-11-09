param(
    [string]$RepositoryPath = 'E:\Rukaya Race OpenSource Branch',
    [string]$Version = 'preview',
    [string]$NotesFile,
    [ValidateSet('Both', 'GitHub', 'Steam')][string]$Target = 'Both',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$config = Get-Content (Join-Path $PSScriptRoot 'workshop.config.json') -Raw | ConvertFrom-Json
$expected = [IO.Path]::GetFullPath($config.mods[0].path)
if ([IO.Path]::GetFullPath($RepositoryPath) -ne $expected) { throw "发布仓库必须是配置的统一工作区：$expected" }
$params = @{ Version = $Version; Target = $Target; Publish = $Publish }
if ($NotesFile) { $params.NotesFile = $NotesFile }
& (Join-Path $PSScriptRoot 'Publish-Release.ps1') @params
