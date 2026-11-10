@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0打开或构建.ps1" -Build
if errorlevel 1 echo 操作未完成，请查看上面的错误信息。
pause
