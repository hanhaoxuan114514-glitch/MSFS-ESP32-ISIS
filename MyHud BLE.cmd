@echo off
chcp 65001 >nul
setlocal
set "PYTHONUTF8=1"
set "PYTHONPYCACHEPREFIX=%~dp0cache\pycache"
title MyHud BLE Test
"%~dp0tools\python311\tools\python.exe" -B "%~dp0windows-bridge\ble_bridge.py" %*
if errorlevel 1 pause
