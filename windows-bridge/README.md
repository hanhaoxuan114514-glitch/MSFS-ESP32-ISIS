# MSFS 2024 → USB 仪表

使用本机已有原生 x64 SimConnect.dll，只读标准 SimVars。默认不打开串口，仅打印 JSON。没有模拟数据模式、网络接收或游戏写入。

在项目根目录 PowerShell 使用已准备好的 Python 3.11：

```powershell
$bridgePython = 'W:/tools/espressif/python_env/idf5.3_py3.11_env/Scripts/python.exe'
& $bridgePython windows-bridge/bridge.py --once
& $bridgePython windows-bridge/bridge.py
```

`--once` 最多等待 15 秒，只打印一条实时帧后退出；超时打印断开帧、返回码 2。即使指定 `--port` 或配置包含串口，`--once` 也不会打开串口。诊断日志发到 stderr，stdout 只有协议 JSON。

固件烧录完成、串口监视器关闭后，启动 USB 发送（Ctrl+C 退出）：

```powershell
& $bridgePython windows-bridge/bridge.py --config windows-bridge/config.example.json
# 等价：
& $bridgePython windows-bridge/bridge.py --port COM6 --hz 20
```

运行游戏的权限与 bridge 应保持一致。当前环境普通受限调用返回 `SimConnect_Open 0x80004005`，提升权限的只读测试成功；因此在本机无法连接时，使用能访问当前游戏会话的 PowerShell 再运行上述命令。

DLL 可用 `--dll`、配置 `dll` 或 `SIMCONNECT_DLL` 环境变量指定绝对路径。自动发现优先当前已验证的 BeyondATC DLL，其次 Volanta。直接从原安装目录加载，不复制 DLL。DLL 的 x64 原生导出与 Microsoft 托管封装不同，不能传入 `Microsoft.FlightSimulator.SimConnect.dll`。

配置字段见示例；命令行覆盖配置。`--pitch-sign` 和 `--bank-sign` 默认均为 -1；符号验证限制详见 [数据来源说明](../docs/simconnect-data.md)。`--baud` 默认 115200，USB Serial/JTAG 虚拟串口仍接受该参数。

按 SIM_FRAME 请求数据，USB/打印输出默认 20 Hz。保持同一 session 严格递增 seq；打开串口/串口重连先发断开帧。SimConnect 退出、异常、非法数据或 1.2 秒无新样本，发断开状态并每 2 秒重连；串口错误每 2 秒重连。Ctrl+C 尝试发送最后断开帧并关闭连接。设备另有独立 1.5 秒超时。模拟器暂停时若仍提供新帧，显示最后由模拟器持续提供的真实状态。

验证：

```powershell
& $bridgePython -m unittest discover -s tests -p 'test_bridge_*.py' -v
```

没有新增全局 Python 包、外部安装或下载；仅复用项目本地 Python 3.11 / pyserial 3.5 和用户已有 DLL。
