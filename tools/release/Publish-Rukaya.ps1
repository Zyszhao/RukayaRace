param(
    [string]$Version = 'preview',
    [string]$NotesFile,
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
# 兼容旧命令名，统一走干净暂存；正式发布须提供版本与更新说明文件。
$params = @{ Version = $Version; Target = 'Steam'; Publish = $Publish }
if ($NotesFile) { $params.NotesFile = $NotesFile }
& (Join-Path $PSScriptRoot 'Publish-Release.ps1') @params
