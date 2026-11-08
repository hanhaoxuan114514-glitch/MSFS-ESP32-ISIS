"""盘点项目目录文件长度，并更新根目录 README 的体积表。"""
from pathlib import Path
from datetime import datetime, timezone
import json
import os

root = Path(__file__).resolve().parent.parent
directories = (
    'vendor/waveshare-esp32-s3-touch-lcd-4',
    'vendor/waveshare-esp32-s3-touch-lcd-4-v3',
    'tools/esp-idf', 'tools/python311', 'tools/espressif',
    'downloads', 'cache', 'esp32-firmware', 'artifacts', 'assets', 'logs', 'backups',
)
rows = []
for name in directories:
    size = count = 0
    for base, _, files in os.walk(root / name):
        for filename in files:
            size += (Path(base) / filename).stat().st_size
            count += 1
    rows.append({'path': name, 'bytes': size, 'mib': round(size / 1048576, 2), 'files': count})
measured_at = datetime.now(timezone.utc).isoformat(timespec='seconds')
report = {
    'measured_at_utc': measured_at,
    'method': '文件长度之和，不含文件系统分配开销；仅盘点本工作文件夹',
    'directories': rows,
}
(root / 'docs/storage-inventory.json').write_text(
    json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8'
)
readme = root / 'README.md'
content = readme.read_text(encoding='utf-8').split('\n<!-- STORAGE-INVENTORY -->')[0]
content += '\n<!-- STORAGE-INVENTORY -->\n## 实际体积盘点\n\n'
content += f'测量时点（UTC）：{measured_at}。以下均在工作文件夹内；W: 是同一位置。\n\n'
content += '| 相对路径 | 字节数 | MiB |\n| --- | ---: | ---: |\n'
for row in rows:
    content += f"| `{row['path']}/` | {row['bytes']:,} | {row['mib']:,.2f} |\n"
content += '\n项目外部安装情况见上文维护记录。详细文件数量见 `docs/storage-inventory.json`。'
content += '\n可运行 `tools/python311/tools/python.exe scripts/update-storage.py` 更新本表。\n'
readme.write_text(content, encoding='utf-8')
print(f'体积盘点已更新，所列目录合计 {sum(row["bytes"] for row in rows):,} 字节。')
