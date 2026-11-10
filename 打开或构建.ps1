param([switch]$Build)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'KitchenChaos'
$unity = $env:UNITY_EDITOR
if (-not $unity) { $unity = Join-Path $env:ProgramFiles 'Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe' }
if (-not (Test-Path -LiteralPath $unity)) {
    throw '未在标准路径找到Unity 2022.3.62f3。请通过Unity Hub添加内层KitchenChaos项目，或设置UNITY_EDITOR为Unity.exe完整路径。'
}
if ($Build) {
    $log = Join-Path $PSScriptRoot 'Windows构建日志.txt'
    Write-Host '请确保同一项目未在Unity编辑器中打开。首次构建需要等待导入包。'
    & $unity -batchmode -quit -projectPath $project -executeMethod KitchenChaos.Multiplayer.MultiplayerTools.BuildWindows -logFile $log
    if ($LASTEXITCODE -ne 0) { throw "构建未成功，请查看 $log" }
    Write-Host "构建完成：$project\Builds\Windows"
} else {
    Start-Process -FilePath $unity -ArgumentList @('-projectPath', ('"' + $project + '"'))
}
