param([string]$Port = 'COM6')
$ErrorActionPreference = 'Stop'
$bridgeRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $bridgeRoot 'scripts/enter-env.ps1')
$bridgeStateFile = Join-Path $bridgeRoot 'logs/bridge-process.json'
if (Test-Path -LiteralPath $bridgeStateFile) {
    $bridgeState = Get-Content -LiteralPath $bridgeStateFile -Raw | ConvertFrom-Json
    $existingBridge = Get-Process -Id $bridgeState.process_id -ErrorAction SilentlyContinue
    if ($existingBridge -and ([DateTimeOffset]$existingBridge.StartTime).UtcDateTime.Ticks -eq
        ([DateTimeOffset]$bridgeState.start_time_utc).UtcDateTime.Ticks) {
        Write-Output "桥接已在运行，端口 $($bridgeState.port)，进程 $($existingBridge.Id)。"
        return
    }
}
$bridgeScript = Join-Path $PSScriptRoot 'bridge.py'
$bridgeConfig = Join-Path $PSScriptRoot 'config.example.json'
$bridgeOut = Join-Path $bridgeRoot 'logs/bridge-live.stdout.log'
$bridgeErr = Join-Path $bridgeRoot 'logs/bridge-live.stderr.log'
$bridgeArgs = @('-u', ('"' + $bridgeScript + '"'), '--config', ('"' + $bridgeConfig + '"'), '--port', $Port)
$bridgeProcess = Start-Process -FilePath 'W:\tools\espressif\python_env\idf5.3_py3.11_env\Scripts\python.exe' `
    -ArgumentList $bridgeArgs -WorkingDirectory $bridgeRoot -WindowStyle Hidden `
    -RedirectStandardOutput $bridgeOut -RedirectStandardError $bridgeErr -PassThru
@{
    process_id = $bridgeProcess.Id
    start_time_utc = $bridgeProcess.StartTime.ToUniversalTime().ToString('o')
    script_path = $bridgeScript
    port = $Port
    stdout_log = $bridgeOut
    stderr_log = $bridgeErr
} | ConvertTo-Json | Set-Content -LiteralPath $bridgeStateFile -Encoding utf8
Write-Output "USB 实时桥接已启动：$Port。日志：$bridgeErr"
