# 构建指南

本仓库是源码仓库。开发工具、下载缓存、设备备份和便携运行环境不随 Git 提交。

## 固件

已验证组合：ESP-IDF 5.3.4、Waveshare BSP 1.1.0、LVGL 8.4.0、ESP32-S3 / 16 MB Flash / 8 MB PSRAM。Component Manager 的组件版本见 `esp32-firmware/dependencies.lock`，不要直接升级至不同硬件分支的 BSP。

在 ESP-IDF 环境中，进入 `esp32-firmware`：

```powershell
idf.py set-target esp32s3
idf.py menuconfig
idf.py build
idf.py -p COM6 flash
```

菜单中确认 Flash 为 16 MB、DIO / 80 MHz、八线 80 MHz PSRAM、RGB 双帧缓冲与 LVGL direct mode。保留 USB Serial/JTAG 作为次级控制台，以便桥接取得设备反馈；主控制台使用 UART。项目默认配置来自原始示例，**首次构建需核对这些选项**；上述标准构建流程没有在全新克隆环境中完成验收。

Windows 下 ESP-IDF 对中文路径的部分工具兼容性有限。建议把工作目录放在短的 ASCII 路径，例如 `C:\Dev\msfs-esp32-isis`。开发机原本使用盘符别名和本地工具目录：`scripts/build-hud.ps1`、`enter-env.ps1` 等脚本依赖该布局，不是通用安装器；其他机器需调整路径，或使用标准 ESP-IDF 环境。游戏同时运行时建议限制为单任务编译。

屏幕初始化在 `main/display.c`：沿用官方 BSP 引脚和 TCA9554 背光控制，不初始化触摸，使用硬件翻转。请勿在未核对硬件的情况下烧录到其他修订版。

## Windows 桥接

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r windows-bridge/requirements.txt
.\.venv\Scripts\python.exe windows-bridge/bridge.py --dll "D:/YourPath/SimConnect.dll" --once
```

`--once` 只读一帧，始终不打开串口。实际传输时增加 `--port COM6` 并去掉 `--once`。连接不到游戏时确认已经进入航班、DLL 为原生 x64 版本，以及游戏和桥接能访问同一用户会话。

## Windows 桌面助手

源码在 `windows-bridge/app/`，使用 Windows 现有 .NET Framework 编译器，无额外 UI 包依赖：

```powershell
.\windows-bridge\app\build.ps1
```

构建脚本先生成原创多尺寸图标，再执行进程身份测试并生成根目录 `A350 ISIS 助手.exe`。构建 EXE 不等于已准备运行环境。助手当前会在项目根目录寻找：

```text
tools/python311/tools/python.exe
tools/espressif/python_env/idf5.3_py3.11_env/Lib/site-packages/serial/
windows-bridge/bridge.py
windows-bridge/config.example.json
```

这些是开发机的便携布局；可自行准备兼容的 Python 3.11 与 pyserial，或先直接运行桥接命令行。助手会在 `logs/` 保存设置和日志，在 `cache/` 保存临时文件。

## 便携包

本地准备完以上运行环境后，执行：

```powershell
python scripts/package-desktop.py
```

输出 `artifacts/MyHud-portable/` 和 `artifacts/MyHud-portable.zip`。打包包含本地 Python 与串口库，但不包含 SimConnect.dll；分发时需保留各运行时与第三方库原有许可。运行包尚未作为 GitHub Release 发布。

## 图形资源与测试

`scripts/isis_scene.py` 定义基础矢量场景。更新静态框线可以执行 `python scripts/generate-isis-ui.py --header-only`，不需要图像库。生成历史静态预览需另外准备 Pillow；这些预览不代表最新动态 HUD。

```powershell
python -m unittest discover -s tests -p "test_*.py" -v
```

四发机型、实际杆位变化和画面观感还需要接设备进行实测。请将测试机型、硬件修订、复现步骤和截图一并记录在 Issue 中。
