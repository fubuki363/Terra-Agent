[CmdletBinding()]
param([string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'Overlay\TerraWikiOverlay.csproj'
$output = if ($OutputDirectory) { [System.IO.Path]::GetFullPath($OutputDirectory) }
    else { Join-Path $projectRoot 'dist\TerraWikiOverlay-single' }
$executable = Join-Path $output 'TerraWikiOverlay.exe'
$running = Get-Process -Name TerraWikiOverlay -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and [System.IO.Path]::GetFullPath($_.Path) -eq $executable }
if ($running) { throw "请先从托盘退出此目录的悬浮助手，再发布：$executable" }

dotnet publish $project -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -o $output
if ($LASTEXITCODE -ne 0) {
    throw "悬浮助手发布失败，退出码：$LASTEXITCODE"
}

$revision = git -C $projectRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 版本。' }
$dirty = [bool](git -C $projectRoot status --porcelain --untracked-files=normal)
[ordered]@{
    version = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
    sourceCommit = $revision.Trim()
    sourceModified = $dirty
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'publish-info.json') -Encoding UTF8
Write-Output "发布完成：$executable"
