# 用点号加载：. .\scripts\enter-env.ps1
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$driveRoot = 'W:\'
if (Test-Path -LiteralPath $driveRoot) {
    $expectedMarker = Join-Path $projectRoot 'tools/workspace-id.txt'
    $mappedMarker = 'W:\tools\workspace-id.txt'
    if (!(Test-Path -LiteralPath $mappedMarker) -or
        (Get-Content -LiteralPath $mappedMarker -Raw) -ne (Get-Content -LiteralPath $expectedMarker -Raw)) {
        throw 'W: 已被其他位置占用；请先选择空闲盘符并统一修改本项目环境配置。'
    }
} else {
    & subst W: $projectRoot
    if ($LASTEXITCODE -ne 0) { throw '创建工作文件夹的 W: 别名失败。' }
}
$env:IDF_PATH = 'W:\tools\esp-idf'
$env:IDF_TOOLS_PATH = 'W:\tools\espressif'
$env:IDF_TARGET = 'esp32s3'
$env:PIP_CACHE_DIR = 'W:\cache\pip'
$env:TEMP = 'W:\cache\tmp'
$env:TMP = $env:TEMP
$env:IDF_COMPONENT_CACHE_PATH = 'W:\cache\components'
$env:PYTHONPYCACHEPREFIX = 'W:\cache\pycache'
$env:PYTHONUTF8 = '1'
$env:PYTHONIOENCODING = 'utf-8'
$env:CCACHE_DIR = 'W:\cache\ccache'
Remove-Item Env:IDF_CCACHE_ENABLE -ErrorAction SilentlyContinue
$env:PATH = 'W:\tools\python311\tools;W:\tools\python311\tools\Scripts;' + $env:PATH
foreach ($directory in @($env:TEMP, $env:PIP_CACHE_DIR, $env:IDF_COMPONENT_CACHE_PATH, $env:PYTHONPYCACHEPREFIX)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
. 'W:\tools\esp-idf\export.ps1'
if ($LASTEXITCODE -ne 0) { throw 'ESP-IDF 环境加载失败，请查看安装日志。' }
