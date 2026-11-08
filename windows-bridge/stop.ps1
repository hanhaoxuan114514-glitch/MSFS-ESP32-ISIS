$ErrorActionPreference = 'Stop'
$bridgeRoot = Split-Path -Parent $PSScriptRoot
$bridgeStateFile = Join-Path $bridgeRoot 'logs/bridge-process.json'
if (!(Test-Path -LiteralPath $bridgeStateFile)) {
    Write-Output '没有记录中的桥接进程。'
    return
}
$bridgeState = Get-Content -LiteralPath $bridgeStateFile -Raw | ConvertFrom-Json
$bridgeProcess = Get-Process -Id $bridgeState.process_id -ErrorAction SilentlyContinue
if ($bridgeProcess) {
    if (([DateTimeOffset]$bridgeProcess.StartTime).UtcDateTime.Ticks -ne
        ([DateTimeOffset]$bridgeState.start_time_utc).UtcDateTime.Ticks) {
        throw '进程编号已经被其他进程使用，拒绝停止。'
    }
    $bridgeInfo = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + [int]$bridgeState.process_id)
    $expectedBridgeScript = Join-Path $PSScriptRoot 'bridge.py'
    if (!$bridgeInfo.CommandLine -or !$bridgeInfo.CommandLine.Contains($expectedBridgeScript)) {
        throw '进程命令与本项目桥接不符，拒绝停止。'
    }
    Stop-Process -Id $bridgeProcess.Id
    $null = $bridgeProcess.WaitForExit(2000)
}
Remove-Item -LiteralPath $bridgeStateFile
Write-Output 'USB 实时桥接已停止，设备稍后显示 DATA LOST。'
