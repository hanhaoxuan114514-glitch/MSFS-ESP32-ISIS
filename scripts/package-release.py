"""Assemble V1 release assets from locally verified runtime and firmware files."""
from pathlib import Path
import hashlib
import json
import shutil
import zipfile

root=Path(__file__).resolve().parent.parent
version='v1.0.0'
out=root/'artifacts'/('release-'+version)
out.mkdir(parents=True,exist_ok=True)
shutil.copy2(root/'artifacts/MyHud-portable.zip',out/f'MyHud-Windows-x64-{version}.zip')
firmware=out/'Firmware'
firmware.mkdir(exist_ok=True)
build=root/'esp32-firmware/build-s3'
app=build/'lvgl_demo_v8.bin'
assert hashlib.sha256(app.read_bytes()).digest()==hashlib.sha256((root/'artifacts/usb-hud/lvgl_demo_v8-progress.bin').read_bytes()).digest(),'Firmware differs from flashed progress-bar version'
for source,name in [(app,'app.bin'),(build/'bootloader/bootloader.bin','bootloader.bin'),(build/'partition_table/partition-table.bin','partition-table.bin')]:
    shutil.copy2(source,firmware/name)
for name in ('LICENSE','THIRD_PARTY_NOTICES.md'):
    shutil.copy2(root/name,firmware/name)
shutil.copy2(root/'esp32-firmware/LICENSE.waveshare',firmware/'LICENSE.waveshare')
idf=root/'tools/esp-idf'
if (idf/'LICENSE').exists():
    dest=firmware/'licenses/esp-idf/LICENSE';dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(idf/'LICENSE',dest)
for base,label in [(idf/'components','esp-idf/components'),(root/'esp32-firmware/managed_components','managed_components')]:
    for p in base.rglob('*'):
        if p.is_file() and p.name.upper().startswith(('LICENSE','COPYING','NOTICE','OFL.')):
            dest=firmware/'licenses'/label/p.relative_to(base)
            dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
(firmware/'flash_args.txt').write_text('--flash_mode dio --flash_freq 80m --flash_size 16MB\n0x0 bootloader.bin\n0x8000 partition-table.bin\n0x10000 app.bin\n',encoding='utf-8')
(firmware/'README.txt').write_text('''A350 ISIS 固件 V1 / v1.0.0

适用：已验证的 Waveshare ESP32-S3 4 英寸 480×480、16 MB Flash、8 MB PSRAM、
V3 示例 / TCA9554 背光控制的无触摸设备。默认 180° 显示。
不同硬件修订版不能仅按产品名称判断兼容，请先核对实际板型。

烧录前在助手里停止桥接，并关闭串口监视器。不要同时占用烧录端口。
以下示例需要可用的 esptool 4.x 环境；本固件包不包含烧录工具。
串口 COM6 仅为示例，请替换为实际串口。命令在本目录内执行。

已有本项目相同分区的设备，仅更新应用：
python -m esptool --chip esp32s3 --port COM6 --baud 460800 write_flash --flash_mode dio --flash_freq 80m --flash_size 16MB 0x10000 app.bin

首次使用本项目分区布局：
python -m esptool --chip esp32s3 --port COM6 --baud 460800 write_flash --flash_mode dio --flash_freq 80m --flash_size 16MB 0x0 bootloader.bin 0x8000 partition-table.bin 0x10000 app.bin

烧录后重新开启 MyHud 桥接，MSFS 进入航班后开始显示实时数据。
固件已在开发用无触摸设备烧录并通过校验；其他板型及干净设备完整烧录流程仍需自行核对。
源码及第三方组件版本见 GitHub 标签 v1.0.0。
''',encoding='utf-8-sig')
manifest={p.name:{'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in firmware.glob('*.bin')}
(firmware/'firmware-manifest.json').write_text(json.dumps({'version':version,'chip':'esp32s3','images':manifest},indent=2),encoding='utf-8')
firmware_zip=out/f'ESP32S3-ISIS-Firmware-{version}.zip'
with zipfile.ZipFile(firmware_zip,'w',compression=zipfile.ZIP_DEFLATED) as z:
    for p in sorted(firmware.rglob('*')):
        if p.is_file():z.write(p,Path('Firmware')/p.relative_to(firmware))
assets=[out/f'MyHud-Windows-x64-{version}.zip',firmware_zip]
(out/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in assets),encoding='ascii')
for p in assets:print(p.name,p.stat().st_size)
