"""有限时间读取串口日志；打开 USB 串口可能导致开发板复位。"""
import argparse
import time
import serial

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--port', default='COM6')
parser.add_argument('--seconds', type=float, default=20)
parser.add_argument('--reset', action='store_true', help='读取前主动复位以捕获完整启动日志')
args = parser.parse_args()
if not 1 <= args.seconds <= 60:
    parser.error('读取时间必须为 1–60 秒')
if args.reset:
    from esptool.cmds import detect_chip
    loader = detect_chip(port=args.port, baud=115200)
    loader.hard_reset()
    port = loader._port
    port.timeout = 0.5
else:
    port = serial.Serial(port=None, baudrate=115200, timeout=0.5)
    port.dtr = False
    port.rts = False
    port.port = args.port
try:
    if not port.is_open:
        port.open()
    deadline = time.monotonic() + args.seconds
    while time.monotonic() < deadline:
        line = port.readline()
        if line:
            print(line.decode('utf-8', errors='replace').rstrip(), flush=True)
finally:
    port.close()
