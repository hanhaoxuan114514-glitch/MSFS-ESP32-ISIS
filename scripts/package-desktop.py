"""Build a self-contained local runtime bundle; no downloads or system installs."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile

root = Path(__file__).resolve().parent.parent
bundle = root / 'artifacts' / 'MyHud-portable'
bundle.mkdir(parents=True, exist_ok=True)
shutil.copy2(root / 'A350 ISIS 助手.exe', bundle / 'A350 ISIS 助手.exe')
bridge = bundle / 'windows-bridge'
bridge.mkdir(exist_ok=True)
for name in ('bridge.py', 'ble_bridge.py', 'ble_fields.def', 'radar_feed.py', 'config.example.json'):
    shutil.copy2(root / 'windows-bridge' / name, bridge / name)
config=json.loads((bridge/'config.example.json').read_text(encoding='utf-8-sig'))
config['dll']=None
(bridge/'config.example.json').write_text(json.dumps(config,indent=2)+'\n',encoding='utf-8')
shutil.copytree(root / 'windows-bridge/map', bridge / 'map', dirs_exist_ok=True)
for name in ('LICENSE','THIRD_PARTY_NOTICES.md'):
    shutil.copy2(root/name,bundle/name)
shutil.copy2(root/'windows-bridge/LICENSE.pyserial',bridge/'LICENSE.pyserial')
ignore = shutil.ignore_patterns('__pycache__', '*.pyc', 'include', 'libs', 'Tools')
shutil.copytree(root / 'cache/wireless-bench/host-deps', bridge / 'ble-deps',
                ignore=shutil.ignore_patterns('__pycache__', '*.pyc'), dirs_exist_ok=True)
shutil.copytree(root / 'tools/python311/tools', bundle / 'tools/python311/tools',
                ignore=ignore, dirs_exist_ok=True)
packages = Path('tools/espressif/python_env/idf5.3_py3.11_env/Lib/site-packages')
for name in ('serial', 'pyserial-3.5.dist-info'):
    shutil.copytree(root / packages / name, bundle / packages / name,
                    ignore=shutil.ignore_patterns('__pycache__', '*.pyc'), dirs_exist_ok=True)
(bundle / 'README.txt').write_text('''A350 ISIS 助手 · USB 与蓝牙

双击本文件夹内的 A350 ISIS 助手.exe，助手会按上次选择的“USB 有线”或“蓝牙 BLE”自动开启桥接。
USB 模式需要选择设备串口，例如 COM6。蓝牙模式自动寻找 MyHud BLE，需要支持 BLE 的屏幕固件。
运行时可以切换模式，助手会停止当前连接并按新模式重新连接。
在左侧切换 HUD 和 ADS-B；在线街道图、全球城市和机场搜索均位于 ADS-B 页面。点击左下角齿轮可选择强调色。
离线底图、地点索引和图钉可离线使用。真实飞机由电脑从 adsb.fi 获取，需要网络。
模拟器进入航班后，程序会将数据发送到屏幕。
关闭窗口会收到系统托盘；右键托盘可开启、停止或退出。
左下角齿轮菜单可开关「登录 Windows 时自动启动」，开启后登录 Windows 时会在系统托盘启动桥接。
启动此副本之前，请先从托盘退出工作文件夹里的旧副本，避免争用串口。

此文件夹包含 Python、串口库和蓝牙库，不依赖开发工作文件夹或驱动器别名。
请保留 tools 和 windows-bridge 文件夹；日志及设置会写在本文件夹 logs 下。
Windows .NET Framework 使用系统现有版本，不需要额外安装。

SimConnect.dll 不包含在包内。程序会尝试发现本机已有的 BeyondATC / Volanta DLL；
如果无法发现，请编辑 windows-bridge/config.example.json，把 dll 改为原生 x64 DLL 的绝对路径。
如使用管理员权限运行游戏，请确认助手具有访问同一游戏会话的权限。
无需 Wi-Fi，无需向模拟器安装额外插件。

自有代码采用 MIT 许可。Python、pyserial 等第三方运行时保留各自许可，见 LICENSE、
THIRD_PARTY_NOTICES.md、tools/python311/tools/LICENSE.txt 和 windows-bridge/LICENSE.pyserial。
蓝牙库许可证随各包保存在 windows-bridge/ble-deps 的 .dist-info 目录中。
''', encoding='utf-8-sig')
files = sorted(p for p in bundle.rglob('*') if p.is_file() and p.name != 'package-manifest.json')
manifest = {p.relative_to(bundle).as_posix(): {'bytes': p.stat().st_size,
             'sha256': hashlib.sha256(p.read_bytes()).hexdigest()} for p in files}
(bundle / 'package-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
archive = root / 'artifacts' / 'MyHud-portable.zip'
with zipfile.ZipFile(archive, 'w', compression=zipfile.ZIP_DEFLATED) as z:
    for p in sorted(bundle.rglob('*')):
        if p.is_file(): z.write(p, Path('MyHud') / p.relative_to(bundle))
print(json.dumps({'bundle': str(bundle), 'files': len(manifest)+1,
                  'bytes': sum(p.stat().st_size for p in bundle.rglob('*') if p.is_file()),
                  'zip_bytes': archive.stat().st_size}, ensure_ascii=False))
