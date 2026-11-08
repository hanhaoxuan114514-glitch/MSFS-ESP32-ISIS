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
for name in ('bridge.py', 'config.example.json'):
    shutil.copy2(root / 'windows-bridge' / name, bridge / name)
ignore = shutil.ignore_patterns('__pycache__', '*.pyc', 'include', 'libs', 'Tools')
shutil.copytree(root / 'tools/python311/tools', bundle / 'tools/python311/tools',
                ignore=ignore, dirs_exist_ok=True)
packages = Path('tools/espressif/python_env/idf5.3_py3.11_env/Lib/site-packages')
for name in ('serial', 'pyserial-3.5.dist-info'):
    shutil.copytree(root / packages / name, bundle / packages / name,
                    ignore=shutil.ignore_patterns('__pycache__', '*.pyc'), dirs_exist_ok=True)
(bundle / 'README.txt').write_text('''A350 ISIS 助手 · USB 有线版

双击本文件夹内的 A350 ISIS 助手.exe，选择 COM6，点击“开启桥接”。
模拟器进入航班后，程序会将数据发送到屏幕。
关闭窗口会收到系统托盘；右键托盘可开启、停止或退出。
启动此副本之前，请先从托盘退出工作文件夹里的旧副本，避免争用串口。

此文件夹已包含 Python 和串口库，不依赖原开发工作文件夹或 W: 盘。
请保留 tools 和 windows-bridge 文件夹；日志及设置会写在本文件夹 logs 下。
Windows .NET Framework 使用系统现有版本，不需要额外安装。

SimConnect.dll 继续只读使用本机已有文件：
D:\\Application\\MSFS\\BeyondATC\\BeyondATC_Data\\Plugins\\x86_64\\SimConnect.dll
没有复制此第三方 DLL；如将来移除 BeyondATC，需更新 windows-bridge/config.example.json 的 dll 路径。
无需 Wi-Fi，无需向模拟器安装额外插件。
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
