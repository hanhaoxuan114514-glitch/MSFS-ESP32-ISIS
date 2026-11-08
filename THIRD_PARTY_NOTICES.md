# 第三方组件说明

- 固件基于 Waveshare ESP32-S3-Touch-LCD-4 V3 示例与官方 BSP 适配，原始 Apache-2.0 许可保存在 [LICENSE.waveshare](esp32-firmware/LICENSE.waveshare)。该许可不表示本项目全部自有代码已采用相同许可。
- ESP-IDF、LVGL、Waveshare BSP、显示驱动等通过 ESP-IDF Component Manager 或独立开发环境获取，版本锁定见 `esp32-firmware/dependencies.lock`。它们的源码、工具链和下载缓存未随本仓库重新分发；请遵守各组件原有许可证。
- Windows 桥接使用 Python 与 pyserial，桌面窗口使用系统 .NET Framework。便携运行环境和 EXE 不包含在当前源码提交中。
- SimConnect.dll 由用户已有的 Microsoft Flight Simulator 兼容软件或 SDK 提供。本仓库不分发该 DLL。
- A350、MSFS 和 Waveshare 等名称用于说明兼容对象，不代表本项目获得相应厂商认可或属于官方产品。

本项目自有代码的开源许可证尚未选定。上传源码不自动授予第三方开源授权。
