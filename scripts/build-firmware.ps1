# 始终保持 W: 路径，不让 idf.py 将路径还原为中文。
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'enter-env.ps1')
& cmake -S W:/esp32-firmware -B W:/esp32-firmware/build-s3 -G Ninja `
    -DIDF_TARGET=esp32s3 `
    -DPYTHON=W:/tools/espressif/python_env/idf5.3_py3.11_env/Scripts/python.exe `
    -DPYTHON_DEPS_CHECKED=1 -DCCACHE_ENABLE=0
if ($LASTEXITCODE -ne 0) { throw '配置失败，请查看错误信息。' }
& ninja -C W:/esp32-firmware/build-s3 -j 1
if ($LASTEXITCODE -ne 0) { throw '编译失败，请查看错误信息。' }
