[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'Overlay\TerraWikiOverlay.csproj'
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $projectRoot 'dist\TerraWikiOverlay-single' }

dotnet publish $project -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -o $output
if ($LASTEXITCODE -ne 0) {
    throw "悬浮助手发布失败，退出码：$LASTEXITCODE"
}

Write-Output "发布完成：$(Join-Path $output 'TerraWikiOverlay.exe')"
