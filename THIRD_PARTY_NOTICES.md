# 第三方组件说明

- 固件基于 Waveshare ESP32-S3-Touch-LCD-4 V3 示例与官方 BSP 适配，原始 Apache-2.0 许可保存在 [LICENSE.waveshare](esp32-firmware/LICENSE.waveshare)。该许可不表示本项目全部自有代码已采用相同许可。
- ESP-IDF、LVGL、Waveshare BSP、显示驱动等通过 ESP-IDF Component Manager 或独立开发环境获取，版本锁定见 `esp32-firmware/dependencies.lock`。它们的源码、工具链和下载缓存未随本仓库重新分发；请遵守各组件原有许可证。
- Windows 桥接使用 Python 与 pyserial，桌面窗口使用系统 .NET Framework。便携运行环境和 EXE 不包含在当前源码提交中。
- 蓝牙桥接使用 Bleak 及其 Windows WinRT 依赖。便携包在 `windows-bridge/ble-deps` 保留各依赖的发行元数据和许可证。
- 离线底图使用 [Natural Earth 公共领域数据](https://www.naturalearthdata.com/about/terms-of-use/)。离线机场坐标来自 MatixYo 的 [ESP32 Plane Radar](https://github.com/MatixYo/ESP32-Plane-Radar) 所整理的 OurAirports 数据。实时目标由 [adsb.fi](https://github.com/adsbfi/opendata) 提供。
- 卫星底图使用 [Blue Marble: Next Generation](https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/)，影像来源为 NASA Earth Observatory。
- 在线街道图 © [OpenStreetMap contributors](https://www.openstreetmap.org/copyright)，瓦片使用遵循 [OSMF 瓦片政策](https://operations.osmfoundation.org/policies/tiles/)。
- 全球机场索引来自 [OurAirports](https://ourairports.com/data/) 公共领域数据，城市索引来自 [GeoNames cities500](https://www.geonames.org/export/)（[CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)）。项目将这些数据转换为本地搜索索引。
- 侧栏仪表和雷达图标改编自 [Lucide](https://lucide.dev/)，许可证见 `windows-bridge/map/LICENSE.lucide.txt`。
- Release 运行包包含 Python 原始许可证和 [pyserial 3.5 的 BSD-3-Clause 许可证](windows-bridge/LICENSE.pyserial)；固件包保留可用的组件许可证与声明。项目自有代码的 MIT 许可不改变这些第三方条件。
- SimConnect.dll 由用户已有的 Microsoft Flight Simulator 兼容软件或 SDK 提供。本仓库不分发该 DLL。
- A350、MSFS 和 Waveshare 等名称用于说明兼容对象，不代表本项目获得相应厂商认可或属于官方产品。

本项目自有代码采用根目录 [MIT License](LICENSE)。MIT 许可不替代第三方组件及衍生部分的原始许可；对应版权、归属和许可条件继续保留。
