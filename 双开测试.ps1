$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot 'KitchenChaos\Builds\Windows\KitchenChaos.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw '尚未生成Windows版本。先使用Unity菜单 Kitchen Chaos > Build Windows Multiplayer。' }
Start-Process -FilePath $exe -ArgumentList @('-screen-fullscreen','0','-screen-width','1280','-screen-height','800')
Start-Process -FilePath $exe -ArgumentList @('-screen-fullscreen','0','-screen-width','1280','-screen-height','800')
Write-Host '一个窗口创建房间，另一个用127.0.0.1和相同端口、房间码加入。两个窗口都准备后由房主开始。'
