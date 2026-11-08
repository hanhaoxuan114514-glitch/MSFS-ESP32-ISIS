# W: 已被用户的 WSL 映射占用时，使用 V:，不更改 W:。
$ErrorActionPreference='Stop'
$hudRoot=Split-Path -Parent $PSScriptRoot
if(!(Test-Path 'V:\')) { & subst V: $hudRoot; if($LASTEXITCODE -ne 0){throw '创建 V: 别名失败'} }
if(!(Test-Path 'V:\tools\workspace-id.txt') -or (Get-Content 'V:\tools\workspace-id.txt' -Raw) -ne (Get-Content (Join-Path $hudRoot 'tools\workspace-id.txt') -Raw)){throw 'V: 不是本项目'}
$env:IDF_PATH='V:\tools\esp-idf';$env:IDF_TOOLS_PATH='V:\tools\espressif';$env:IDF_TARGET='esp32s3'
$env:IDF_PYTHON_ENV_PATH='V:\tools\espressif\python_env\idf5.3_py3.11_env'
$env:ESP_ROM_ELF_DIR='V:\tools\espressif\tools\esp-rom-elfs\20240305'
$env:TEMP='V:\cache\tmp';$env:TMP=$env:TEMP;$env:PIP_CACHE_DIR='V:\cache\pip';$env:IDF_COMPONENT_CACHE_PATH='V:\cache\components'
$env:PYTHONUTF8='1';$env:PYTHONIOENCODING='utf-8';$env:PYTHONPYCACHEPREFIX='V:\cache\pycache'
$hudBins=Get-ChildItem 'V:\tools\espressif\tools' -Filter '*.exe' -Recurse | Select-Object -ExpandProperty DirectoryName -Unique
$env:PATH='V:\tools\python311\tools;'+($hudBins -join ';')+';'+$env:PATH
& 'V:\tools\python311\tools\python.exe' (Join-Path $PSScriptRoot 'relocate-build.py')
if($LASTEXITCODE -ne 0){throw '构建缓存路径迁移失败'}
& cmake -S V:/esp32-firmware -B V:/esp32-firmware/build-s3 -G Ninja -DIDF_TARGET=esp32s3 -DPYTHON=V:/tools/espressif/python_env/idf5.3_py3.11_env/Scripts/python.exe -DPYTHON_DEPS_CHECKED=1 -DCCACHE_ENABLE=0
if($LASTEXITCODE -ne 0){throw '配置失败'}
& ninja -C V:/esp32-firmware/build-s3 -j 1
if($LASTEXITCODE -ne 0){throw '编译失败'}
