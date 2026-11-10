$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
$parent = 'E:\Users\HP\Desktop\大三课程\实训'
$target = Join-Path $parent 'KitchenChaos-Multiplayer'
if ($source -eq $target) { Write-Host "工程已位于 $target"; exit 0 }
if (-not (Test-Path -LiteralPath 'E:\')) { throw '没有找到E盘，请手动将完整文件夹复制到所需位置。' }
New-Item -ItemType Directory -Path $parent -Force | Out-Null
if (Test-Path -LiteralPath $target) { $target = Join-Path $parent ('KitchenChaos-Multiplayer-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
New-Item -ItemType Directory -Path $target | Out-Null
Get-ChildItem -LiteralPath $source -Force | Where-Object { $_.Name -ne '.git' } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse -Force
}
Write-Host "已复制完整工程到：$target"
Write-Host "在 Unity Hub 中添加：$(Join-Path $target 'KitchenChaos')"
Start-Process explorer.exe -ArgumentList @($target)
