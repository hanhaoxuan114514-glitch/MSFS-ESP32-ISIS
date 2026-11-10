"""Windows BLE transport for the MyHud read-only SimConnect bridge."""
import argparse
import asyncio
import json
from pathlib import Path
import re
import struct
import subprocess
import sys

DIRECTORY = Path(__file__).resolve().parent
DEPENDENCIES = DIRECTORY / 'ble-deps'
if not DEPENDENCIES.is_dir():
    DEPENDENCIES = DIRECTORY.parent / 'cache' / 'wireless-bench' / 'host-deps'
DEVICE_NAME = 'MyHud BLE'
CHARACTERISTIC = 'a3501712-6c30-401e-87e0-972442145710'
FIELDS = tuple((key, int(scale)) for key, scale in re.findall(
    r'^FIELD\((\w+), (\d+), (?:NUMBER|BOOL)\)$',
    (DIRECTORY / 'ble_fields.def').read_text(encoding='ascii'), re.MULTILINE))
PACKET_BYTES = 22 + 3 * len(FIELDS)
FIELD_OFFSETS = {key: (22 + 3 * i, scale) for i, (key, scale) in enumerate(FIELDS)}


def pack(frame):
    header = struct.pack('<BB16sI', 1, frame['connected'],
                         bytes.fromhex(frame['session']), frame['seq'])
    data = bytearray(header)
    for key, scale in FIELDS:
        value = frame.get(key)
        fixed = -0x800000 if value is None else round(value * scale)
        data.extend(fixed.to_bytes(3, 'little', signed=True))
    return bytes(data)


async def read_frames(stream, latest):
    while line := await stream.readline():
        latest[0] = pack(json.loads(line))
    raise RuntimeError('SimConnect 桥接已退出，请查看上方输出。')


def status_line(packet):
    seq, = struct.unpack_from('<I', packet, 18)
    status = f'BLE telemetry: connected={packet[1]} seq={seq}'
    if packet[1]:
        values = []
        for key in ('ias_kt', 'altitude_ft', 'heading_deg'):
            offset, scale = FIELD_OFFSETS[key]
            values.append(int.from_bytes(packet[offset:offset+3], 'little', signed=True) / scale)
        status += ' IAS={:.1f} ALT={:.0f} HDG={:.1f}'.format(*values)
    return status


async def send_frames(latest, status_updates=False, radar=None):
    sys.path.insert(0, str(DEPENDENCIES))
    from bleak import BleakClient, BleakScanner
    from bleak.exc import BleakError

    while True:
        linked = False
        print('正在寻找 MyHud BLE 屏幕…', flush=True)
        try:
            device = await BleakScanner.find_device_by_filter(
                lambda _, adv: adv.local_name == DEVICE_NAME, timeout=8)
            if device is None:
                continue
            async with BleakClient(device, timeout=12) as client:
                characteristic = client.services.get_characteristic(CHARACTERISTIC)
                if characteristic is None:
                    raise BleakError('屏幕缺少 MyHud 蓝牙服务')
                for _ in range(20):
                    if characteristic.max_write_without_response_size >= PACKET_BYTES:
                        break
                    await asyncio.sleep(.1)
                else:
                    raise BleakError('蓝牙单包容量不足')
                print(f'蓝牙已连接 · 单包 {PACKET_BYTES} 字节 · 等待飞行数据', flush=True)
                linked = True
                if status_updates:
                    print('BLE link: connected', flush=True)
                sent = None
                live = False
                report_at = 0
                first_radar_packet = True
                loop = asyncio.get_running_loop()
                while client.is_connected:
                    packet = latest[0]
                    if packet is not None and packet is not sent:
                        async with asyncio.timeout(2):
                            await client.write_gatt_char(characteristic, packet, response=False)
                        sent = packet
                        if bool(packet[1]) != live:
                            live = bool(packet[1])
                            print('正在发送实时飞行数据' if live else '等待模拟器数据', flush=True)
                            report_at = 0
                        if status_updates and loop.time() >= report_at:
                            print(status_line(packet), flush=True)
                            report_at = loop.time() + 1
                    radar_packet = radar.packet(force=first_radar_packet) if radar else None
                    if radar_packet:
                        async with asyncio.timeout(2):
                            await client.write_gatt_char(characteristic, radar_packet, response=False)
                        first_radar_packet = False
                    await asyncio.sleep(.008)
        except (BleakError, OSError, TimeoutError) as exc:
            print(f'蓝牙连接中断，稍后重试 · {exc}', flush=True)
        finally:
            if linked and status_updates:
                print('BLE link: disconnected', flush=True)
        await asyncio.sleep(1)


async def run(config, status_updates=False, radar_config=None):
    child_args = ['--allow-no-sim'] if radar_config else []
    proc = await asyncio.create_subprocess_exec(
        sys.executable, '-u', str(DIRECTORY / 'bridge.py'), '--config', str(config), '--port=', *child_args,
        stdout=asyncio.subprocess.PIPE, creationflags=subprocess.CREATE_NO_WINDOW)
    radar = None
    try:
        if radar_config:
            from radar_feed import RadarFeed
            radar = RadarFeed(radar_config)
        latest = [None]
        async with asyncio.TaskGroup() as tasks:
            tasks.create_task(read_frames(proc.stdout, latest))
            tasks.create_task(send_frames(latest, status_updates, radar))
    finally:
        if radar:
            radar.close()
        if proc.returncode is None:
            try:
                proc.terminate()
            except ProcessLookupError:
                pass
            await proc.wait()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, default=DIRECTORY / 'config.example.json')
    parser.add_argument('--status', action='store_true', help='emit desktop status updates')
    parser.add_argument('--radar-config', type=Path)
    args = parser.parse_args()
    try:
        asyncio.run(run(args.config, args.status, args.radar_config))
    except KeyboardInterrupt:
        pass
