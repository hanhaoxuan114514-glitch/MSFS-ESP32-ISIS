"""Read-only native SimConnect to newline JSON / ESP32 USB bridge (Python 3.11)."""
import argparse
import ctypes as C
import json
import math
import os
from pathlib import Path
import struct
import sys
import time
import uuid

# Explicit requested units, not the misleading default units in the SimVar names.
VARIABLES = [
    ('pitch_deg', 'PLANE PITCH DEGREES', 'degrees'),
    ('bank_deg', 'PLANE BANK DEGREES', 'degrees'),
    ('ias_kt', 'AIRSPEED INDICATED', 'knots'),
    ('altitude_ft', 'INDICATED ALTITUDE', 'feet'),
    ('heading_deg', 'PLANE HEADING DEGREES MAGNETIC', 'degrees'),
    ('vertical_speed_fpm', 'VERTICAL SPEED', 'feet per minute'),
    ('qnh_hpa', 'KOHLSMAN SETTING MB:1', 'millibars'),
    ('on_ground', 'SIM ON GROUND', 'bool'),
    ('mach', 'AIRSPEED MACH', 'mach'),
    ('latitude_deg', 'PLANE LATITUDE', 'degrees'),
    ('longitude_deg', 'PLANE LONGITUDE', 'degrees'),
    ('throttle1_pct', 'GENERAL ENG THROTTLE LEVER POSITION:1', 'percent'),
    ('throttle2_pct', 'GENERAL ENG THROTTLE LEVER POSITION:2', 'percent'),
    ('reverse1_pct', 'TURB ENG REVERSE NOZZLE PERCENT:1', 'percent'),
    ('reverse2_pct', 'TURB ENG REVERSE NOZZLE PERCENT:2', 'percent'),
    ('fuel_quantity', 'FUEL TOTAL QUANTITY', 'gallons'),
    ('fuel_capacity', 'FUEL TOTAL CAPACITY', 'gallons'),
    ('engine_count', 'NUMBER OF ENGINES', 'number'),
    ('throttle3_pct', 'GENERAL ENG THROTTLE LEVER POSITION:3', 'percent'),
    ('throttle4_pct', 'GENERAL ENG THROTTLE LEVER POSITION:4', 'percent'),
    ('reverse3_pct', 'TURB ENG REVERSE NOZZLE PERCENT:3', 'percent'),
    ('reverse4_pct', 'TURB ENG REVERSE NOZZLE PERCENT:4', 'percent'),
    ('flaps_index', 'FLAPS HANDLE INDEX', 'number'),
    ('flaps_positions', 'FLAPS NUM HANDLE POSITIONS', 'number'),
    ('tas_kt', 'AIRSPEED TRUE', 'knots'),
    ('engine_type', 'ENGINE TYPE', 'number'),
    ('vs0_kt', 'DESIGN SPEED VS0', 'knots'),
    ('flap_limit_kt', 'FLAPS CURRENT SPEED LIMITATION', 'knots'),
    ('overspeed_kt', 'AIRSPEED BARBER POLE', 'knots'),
    ('prop_rpm', 'PROP RPM:1', 'rpm'),
    ('prop_rpm_pct', 'PROP MAX RPM PERCENT:1', 'percent'),
    ('agl_ft', 'PLANE ALT ABOVE GROUND', 'feet'),
    *[(f'prop_rpm{suffix}_{engine}', f'{name}:{engine}', unit)
      for engine in range(2, 5)
      for suffix, name, unit in (('', 'PROP RPM', 'rpm'), ('_pct', 'PROP MAX RPM PERCENT', 'percent'))],
    ('ground_speed_kt', 'GROUND VELOCITY', 'knots'),
    ('rotor_pct', 'ROTOR RPM PCT:0', 'percent'),
    ('collective_pct', 'ROTOR COLLECTIVE BLADE PITCH PCT', 'percent'),
    ('wind_kt', 'AMBIENT WIND VELOCITY', 'knots'),
    ('aoa_deg', 'INCIDENCE ALPHA', 'degrees'),
    ('trim_pct', 'ELEVATOR TRIM PCT EX2', 'percent'),
    ('flap_deg', 'TRAILING EDGE FLAPS LEFT ANGLE', 'degrees'),
    ('rotor_count', 'NUMBER OF MAIN ROTORS', 'number'),
    ('rotor1_rpm', 'ROTOR RPM:1', 'rpm'),
    ('rotor2_rpm', 'ROTOR RPM:2', 'rpm'),
    ('rotor2_pct', 'ROTOR RPM PCT:2', 'percent'),
    *[(f'{key}_{engine}', f'{name}:{engine}', unit)
      for engine in range(1, 5)
      for key, name, unit in (('power', 'TURB ENG MAX TORQUE PERCENT', 'percent'),
                              ('eng_rpm', 'GENERAL ENG RPM', 'rpm'),
                              ('eng_pct', 'GENERAL ENG PCT MAX RPM', 'percent'))],
    ('g_load', 'G FORCE', 'GForce'),
    ('parking_brake', 'BRAKE PARKING POSITION', 'bool'),
    ('parking_brake_available', 'PARKING BRAKE AVAILABLE', 'bool'),
    ('left_brake', 'BRAKE LEFT POSITION', 'position 32k'),
    ('right_brake', 'BRAKE RIGHT POSITION', 'position 32k'),
]
RANGES = dict(pitch_deg=(-90, 90), bank_deg=(-180, 180), ias_kt=(0, 1500),
              altitude_ft=(-2000, 100000), heading_deg=(0, 360),
              vertical_speed_fpm=(-150000, 150000), qnh_hpa=(700, 1200),
              mach=(0, 5), latitude_deg=(-90, 90), longitude_deg=(-180, 180),
              throttle1_pct=(-100,100), throttle2_pct=(-100,100),
              reverse1_pct=(0,100), reverse2_pct=(0,100))


def aircraft_kind(engine_type, aircraft_class, category, sim_category):
    kinds = {
        'helicopter': 2, 'rotorcraft': 2, 'glider': 3, 'sailplane': 3,
        'airship': 4, 'hotairballoon': 5, 'balloon': 5, 'lighterthanair': 6,
        'gyroplane': 7, 'tiltrotor': 8, 'ornithopter': 9,
    }
    for value in (category, sim_category, aircraft_class):
        key = ''.join(c for c in value.lower() if c.isalnum())
        if key in kinds and kinds[key] != 6:
            return kinds[key]
    if aircraft_class.upper() == 'LIGHTERTHANAIR':
        return 6
    if aircraft_class.upper() == 'OTHER':
        return 10
    return {1: 0, 0: 1, 5: 1, 3: 2, 6: 11}.get(engine_type, 10)


def normalize(values, pitch_sign=-1, bank_sign=-1, aircraft_class='', category='', sim_category='',
              aircraft_title='', flap_limits=None, observed_flap_positions=0):
    if len(values) != len(VARIABLES):
        raise ValueError('wrong SimConnect datum count')
    data = dict(zip((v[0] for v in VARIABLES), values))
    if not math.isfinite(data['on_ground']):
        raise ValueError('invalid on_ground')
    data['on_ground'] = data['on_ground'] != 0
    g_load = data.pop('g_load')
    if -99.9 <= g_load <= 99.9:
        data['g_load'] = round(g_load, 2)
    parking_brake = data.pop('parking_brake')
    parking_available = data.pop('parking_brake_available')
    wheel_brakes = (data.pop('left_brake'), data.pop('right_brake'))
    if math.isfinite(parking_available) and parking_available != 0 and math.isfinite(parking_brake):
        data['parking_brake'] = parking_brake != 0
        # Ignore the first 1% of pedal travel to avoid flicker from noisy axes.
        data['wheel_brake'] = any(327.67 < position <= 32768 for position in wheel_brakes)
    data['pitch_deg'] *= pitch_sign
    data['bank_deg'] *= bank_sign
    if math.isfinite(data['heading_deg']):
        data['heading_deg'] %= 360
    for key, (low, high) in RANGES.items():
        value = data[key]
        if isinstance(value, bool) or not math.isfinite(value) or not low <= value <= high:
            if key in ('vertical_speed_fpm', 'mach', 'latitude_deg', 'longitude_deg',
                       'throttle1_pct', 'throttle2_pct', 'reverse1_pct', 'reverse2_pct'):
                data.pop(key)
            else:
                raise ValueError('invalid ' + key)
    if ('latitude_deg' in data) != ('longitude_deg' in data):
        data.pop('latitude_deg', None)
        data.pop('longitude_deg', None)
    count=data.pop('engine_count')
    valid_count=math.isfinite(count) and count==int(count) and 0<=count<=4
    if valid_count:
        data['engine_count']=int(count)
    for engine in range(1,5):
        for prefix,lo,hi in [('throttle',-100,100),('reverse',0,100)]:
            key=f'{prefix}{engine}_pct'
            value=data.get(key)
            if not valid_count or engine>count or value is None or not math.isfinite(value) or not lo<=value<=hi:
                data.pop(key,None)
    quantity, capacity = data.pop('fuel_quantity'), data.pop('fuel_capacity')
    if math.isfinite(quantity) and math.isfinite(capacity) and capacity > 0 and 0 <= quantity <= capacity:
        data['fuel_pct'] = 100 * quantity / capacity
    index, positions = data.pop('flaps_index'), data.pop('flaps_positions')
    if math.isfinite(positions) and 1 <= positions <= 32 and positions == int(positions):
        observed_flap_positions = max(observed_flap_positions, int(positions))
    if math.isfinite(index) and 0 <= index < 32 and index == int(index):
        index = int(index)
        if index > 0 or observed_flap_positions:
            data.update(flaps_index=index, flaps_positions=max(observed_flap_positions, index + 1))
    tas = data['tas_kt']
    if not math.isfinite(tas) or not 0 <= tas <= 3000:
        data.pop('tas_kt')
    engine_type = data.pop('engine_type')
    kind = aircraft_kind(engine_type, aircraft_class, category, sim_category)
    if kind == 1 and (not valid_count or count == 0):
        kind = 10
    propeller = kind == 1
    data['aircraft_kind'] = kind
    data['propeller'] = propeller
    vs0 = data.pop('vs0_kt')
    flap_limit = data.pop('flap_limit_kt')
    if 0 < vs0 <= 1500:
        data['vs0_kt'] = round(vs0, 1)
        detent = data.get('flaps_index')
        if aircraft_title and flap_limits is not None and detent is not None:
            if detent > 0 and vs0 < flap_limit <= 1500:
                flap_limits[detent] = flap_limit
            elif not vs0 < flap_limit <= 1500:
                lookup = detent if detent > 0 else data['flaps_positions'] - 1
                flap_limit = flap_limits.get(lookup, 0)
        if vs0 < flap_limit <= 1500:
            data['flap_limit_kt'] = round(flap_limit, 1)
    if not 0 < data['overspeed_kt'] <= 1500:
        data.pop('overspeed_kt')
    for engine in range(1, 5):
        suffix = '' if engine == 1 else f'_{engine}'
        for field, maximum, precision in (('prop_rpm', 15000, 0), ('prop_rpm_pct', 200, 1)):
            key = field + suffix
            value = data[key]
            if kind == 4:
                value = data[f'eng_{"rpm" if field == "prop_rpm" else "pct"}_{engine}']
            if (propeller or kind == 4) and valid_count and engine <= count and 0 <= value <= maximum:
                data[key] = round(value, precision)
            else:
                data.pop(key)
        data.pop(f'eng_rpm_{engine}')
        data.pop(f'eng_pct_{engine}')
        power = data.pop(f'power_{engine}')
        if kind in (2, 7, 8) and valid_count and engine <= count and 0 <= power <= 200:
            data[f'power_{engine}'] = round(power, 1)
    rotor_count = data.pop('rotor_count')
    if kind in (2, 7, 8) and rotor_count in (1, 2):
        data['rotor_count'] = int(rotor_count)
    for key, low, high, enabled in (
        ('aoa_deg', -90, 90, propeller), ('trim_pct', -100, 100, propeller),
        ('flap_deg', 0, 90, propeller and positions > 1),
        ('rotor1_rpm', 0, 3000, rotor_count in (1, 2) and kind in (2, 7, 8)),
        ('rotor2_rpm', 0, 3000, rotor_count == 2 and kind in (2, 7, 8)),
        ('rotor2_pct', 0, 200, rotor_count == 2 and kind in (2, 7, 8)),
    ):
        value = data.pop(key)
        if enabled and low <= value <= high:
            data[key] = round(value, 1)
    agl = data.pop('agl_ft')
    if data['on_ground']:
        data['agl_ft'] = 0
    elif 0 <= agl <= 200000:
        data['agl_ft'] = round(agl)
    for key, maximum in (('ground_speed_kt', 2000), ('rotor_pct', 200),
                         ('collective_pct', 100), ('wind_kt', 300)):
        value = data.pop(key)
        if 0 <= value <= maximum and (key not in ('rotor_pct', 'collective_pct') or kind in (2, 7, 8)):
            data[key] = round(value, 1)
    return data


class Protocol:
    def __init__(self):
        self.session = uuid.uuid4().hex
        self.seq = 0

    def line(self, data=None):
        # Never wrap within a session: exit safely after ~6.8 years at 20 Hz.
        if self.seq > 0xffffffff:
            raise OverflowError('sequence exhausted; restart bridge')
        packet = dict(version=1, session=self.session, seq=self.seq,
                      connected=data is not None, demo=False)
        if data is not None:
            packet.update(data)
        line = json.dumps(packet, separators=(',', ':'), allow_nan=False).encode('ascii')
        if len(line) > 1024:
            # Four-engine frames can exceed the USB limit with full float precision.
            compact = {key: round(value, 4) if isinstance(value, float) else value
                       for key, value in packet.items()}
            line = json.dumps(compact, separators=(',', ':'), allow_nan=False).encode('ascii')
        if len(line) > 1024:
            raise ValueError('packet exceeds 1024 bytes')
        self.seq += 1
        return line + b'\n'


def discover_dll():
    candidates = [os.getenv('SIMCONNECT_DLL', ''),
        'D:/Application/MSFS/BeyondATC/BeyondATC_Data/Plugins/x86_64/SimConnect.dll',
        str(Path(os.getenv('APPDATA', '')) / 'Orbx/Volanta/plugins/SimConnect.dll')]
    return next((str(Path(p).resolve()) for p in candidates if p and Path(p).is_file()), None)


class SimConnect:
    def __init__(self, dll_path):
        if os.name != 'nt' or C.sizeof(C.c_void_p) != 8:
            raise OSError('64-bit Windows Python required')
        path = Path(dll_path).resolve(strict=True)
        self.dll = C.WinDLL(str(path))
        handle, uint = C.c_void_p, C.c_uint32
        signatures = {
            'Open': [C.POINTER(handle), C.c_char_p, handle, uint, handle, uint],
            'Close': [handle],
            'AddToDataDefinition': [handle, uint, C.c_char_p, C.c_char_p, uint, C.c_float, uint],
            'RequestDataOnSimObject': [handle] + [uint] * 8,
            'GetNextDispatch': [handle, C.POINTER(handle), C.POINTER(uint)],
        }
        for name, args in signatures.items():
            fn = getattr(self.dll, 'SimConnect_' + name)
            fn.argtypes, fn.restype = args, C.c_int32
        self.handle = handle()
        self.aircraft_class = self.category = self.sim_category = self.aircraft_title = ''
        self.flap_limits = {}
        self.observed_flap_positions = 0
        self.smoothed_aoa = None
        try:
            self.call('Open', C.byref(self.handle), b'A359 USB read-only', None, 0, None, 0)
            for _, name, unit in VARIABLES:
                self.call('AddToDataDefinition', self.handle, 1, name.encode(), unit.encode(), 4, 0, 0xffffffff)
            for name in (b'AIRCRAFT OBJECT CLASS', b'AIRCRAFT CATEGORY', b'CATEGORY'):
                self.call('AddToDataDefinition', self.handle, 2, name, None, 6, 0, 0xffffffff)
            self.call('AddToDataDefinition', self.handle, 2, b'TITLE', None, 8, 0, 0xffffffff)
            # Fixed strings in a separate, once-per-second definition.
            self.call('RequestDataOnSimObject', self.handle, 2, 2, 0, 4, 0, 0, 0, 0)
            # USER=0, SIM_FRAME=3, flags=0: all data including unchanged values.
            self.call('RequestDataOnSimObject', self.handle, 1, 1, 0, 3, 0, 0, 0, 0)
        except Exception:
            self.close()
            raise

    def call(self, name, *args):
        result = getattr(self.dll, 'SimConnect_' + name)(*args)
        if result < 0:
            raise OSError(f'SimConnect_{name}: HRESULT 0x{result & 0xffffffff:08X}')

    def poll(self):
        latest = None
        for _ in range(1024):
            pointer, size = C.c_void_p(), C.c_uint32()
            hr = self.dll.SimConnect_GetNextDispatch(self.handle, C.byref(pointer), C.byref(size))
            if hr < 0:
                break  # E_FAIL also means no message available, not a disconnect.
            if size.value < 12 or not pointer.value:
                raise OSError('truncated SimConnect header')
            raw = C.string_at(pointer, size.value)
            length, _, kind = struct.unpack_from('<III', raw)
            if length > len(raw):
                raise OSError('truncated SimConnect message')
            if kind == 3:
                raise OSError('simulator quit')
            if kind == 1:
                if len(raw) < 16:
                    raise OSError('truncated SimConnect exception')
                raise OSError('SimConnect exception ' + str(struct.unpack_from('<I', raw, 12)[0]))
            if kind == 8:  # SIMOBJECT_DATA; packed DWORD header, payload starts at byte 40.
                if len(raw) < 40:
                    raise OSError('truncated data header')
                request, obj, definition, flags, entry, total, count = struct.unpack_from('<7I', raw, 12)
                if request == 1 and definition == 1:
                    if flags or count != len(VARIABLES) or len(raw) < 40 + count * 8:
                        raise OSError('unexpected SimConnect data layout')
                    latest = struct.unpack_from('<' + 'd' * count, raw, 40)
                elif request == 2 and definition == 2 and not flags and count == 4 and len(raw) >= 264:
                    previous_title = getattr(self, 'aircraft_title', '')
                    self.aircraft_class, self.category, self.sim_category, self.aircraft_title = (
                        raw[start:end].split(b'\0', 1)[0].decode('utf-8', 'replace').strip()
                        for start,end in ((40,72),(72,104),(104,136),(136,264)))
                    if previous_title != self.aircraft_title:
                        self.flap_limits = {}
                        self.observed_flap_positions = 0
                        self.smoothed_aoa = None
                        latest = None
        return latest

    def smooth_aoa(self, data, elapsed):
        aoa = data.get('aoa_deg')
        if aoa is not None and self.smoothed_aoa is not None and 0 <= elapsed <= 1:
            aoa = self.smoothed_aoa + (aoa - self.smoothed_aoa) * -math.expm1(-elapsed / 0.3)
        self.smoothed_aoa = aoa
        if aoa is not None:
            data['aoa_deg'] = round(aoa, 2)

    def close(self):
        if self.handle.value:
            self.dll.SimConnect_Close(self.handle)
            self.handle = C.c_void_p()


def open_usb(serial_module, port, baud):
    device = serial_module.Serial(port=None, baudrate=baud, timeout=0, write_timeout=.25)
    try:
        device.dtr = False
        device.rts = False
        device.port = port
        device.open()
        return device
    except Exception:
        device.close()
        raise


class DeviceLog:
    """Bounded device TX lines; discard oversized lines until the next newline."""
    def __init__(self):
        self.clear()

    def clear(self):
        self.pending = bytearray()
        self.discarding = False

    def feed(self, chunk):
        for byte in chunk:
            if byte == 10:
                if not self.discarding and self.pending:
                    text = self.pending.decode('utf-8', errors='replace').rstrip('\r')
                    print('[device] ' + text, file=sys.stderr, flush=True)
                self.clear()
            elif not self.discarding:
                if len(self.pending) < 2048:
                    self.pending.append(byte)
                else:
                    self.pending.clear()
                    self.discarding = True

    def drain(self, device):
        # timeout=0 makes reads nonblocking. Bound work if the device floods TX.
        for _ in range(16):
            available = device.in_waiting
            if not available:
                return
            chunk = device.read(min(available, 4096))
            if not chunk:
                return
            self.feed(chunk)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--config', type=Path, help='JSON configuration; command line overrides it')
    parser.add_argument('--dll', help='absolute path to existing native x64 SimConnect.dll')
    parser.add_argument('--port', help='USB port, e.g. COM6; omitted = print JSON only')
    parser.add_argument('--radar-config', type=Path, help='desktop radar location and mode')
    parser.add_argument('--allow-no-sim', action='store_true', help=argparse.SUPPRESS)
    parser.add_argument('--hz', type=float, default=20)
    parser.add_argument('--baud', type=int, default=115200)
    parser.add_argument('--once', action='store_true', help='print one live frame, never open USB')
    parser.add_argument('--timeout', type=float, default=15, help='--once deadline seconds')
    parser.add_argument('--pitch-sign', type=int, choices=(-1, 1), default=-1)
    parser.add_argument('--bank-sign', type=int, choices=(-1, 1), default=-1)
    preliminary, _ = parser.parse_known_args(argv)
    if preliminary.config:
        config = json.loads(preliminary.config.read_text(encoding='utf-8-sig'))
        allowed = {'dll', 'port', 'hz', 'baud', 'timeout', 'pitch_sign', 'bank_sign'}
        if not isinstance(config, dict) or config.keys() - allowed:
            parser.error('unknown configuration key')
        parser.set_defaults(**config)
    args = parser.parse_args(argv)
    if not 1 <= args.hz <= 60 or not math.isfinite(args.hz) or args.timeout <= 0:
        parser.error('hz must be 1..60 and timeout positive')
    if args.pitch_sign not in (-1, 1) or args.bank_sign not in (-1, 1):
        parser.error('attitude signs must be -1 or 1')
    dll = args.dll or discover_dll()
    if (not dll or not Path(dll).is_absolute()) and not (args.radar_config or args.allow_no_sim):
        parser.error('native SimConnect DLL missing: supply --dll ABSOLUTE_PATH')
    radar = None
    if args.radar_config and args.port and not args.once:
        from radar_feed import RadarFeed
        radar = RadarFeed(args.radar_config)
    protocol, sim, usb = Protocol(), None, None
    device_log = DeviceLog()
    data, last_data, retry, usb_retry = None, 0, 0, 0
    started = time.monotonic()
    def emit(frame):
        nonlocal usb, usb_retry
        if args.port and not args.once:
            if usb is None:
                if time.monotonic() < usb_retry:
                    return
                import serial
                try:
                    usb = open_usb(serial, args.port, args.baud)
                    # Establish disconnected state before live packets, including after reconnect.
                    usb.write(protocol.line())
                except (OSError, serial.SerialException) as exc:
                    print(f'USB unavailable: {exc}', file=sys.stderr)
                    if usb:
                        usb.close()
                    usb = None
                    device_log.clear()
                    usb_retry = time.monotonic() + 2
                    return
            try:
                line = protocol.line(frame)
                if usb.write(line) != len(line):
                    raise OSError('short USB write')
                radar_packet = radar.packet() if radar else None
                if radar_packet:
                    radar_line = b'RADAR ' + radar_packet.hex().encode('ascii') + b'\n'
                    if usb.write(radar_line) != len(radar_line):
                        raise OSError('short USB radar write')
            except OSError as exc:
                print(f'USB disconnected: {exc}', file=sys.stderr)
                usb.close()
                usb, usb_retry = None, time.monotonic() + 2
                device_log.clear()
        else:
            sys.stdout.buffer.write(protocol.line(frame))
            sys.stdout.buffer.flush()
    try:
        if not args.once:
            emit(None)
        while True:
            tick = time.monotonic()
            if dll and sim is None and tick >= retry:
                try:
                    sim = SimConnect(dll)
                    last_data = tick
                    print(f'SimConnect opened: {dll}', file=sys.stderr)
                except (OSError, AttributeError) as exc:
                    print(str(exc), file=sys.stderr)
                    retry = tick + 2
            if sim:
                try:
                    values = sim.poll()
                    if values is not None:
                        data = normalize(values, args.pitch_sign, args.bank_sign,
                                         sim.aircraft_class, sim.category, sim.sim_category, sim.aircraft_title,
                                         sim.flap_limits, sim.observed_flap_positions)
                        if sim.aircraft_title:
                            sim.observed_flap_positions = data.get('flaps_positions', sim.observed_flap_positions)
                        sim.smooth_aoa(data, tick - last_data)
                        last_data = tick
                    if tick - (last_data or started) > 1.2:
                        raise OSError('no fresh simulator data for 1.2 s')
                except (OSError, ValueError) as exc:
                    print(str(exc), file=sys.stderr)
                    sim.close()
                    sim, data, retry = None, None, tick + 2
            if data is not None or not args.once:
                emit(data)
                if args.once:
                    return 0
            if usb:
                try:
                    device_log.drain(usb)
                except OSError as exc:
                    print(f'USB disconnected while reading: {exc}', file=sys.stderr)
                    usb.close()
                    usb, usb_retry = None, time.monotonic() + 2
                    device_log.clear()
            if args.once and tick - started >= args.timeout:
                emit(None)
                return 2
            time.sleep(max(0, 1 / args.hz - (time.monotonic() - tick)))
    except KeyboardInterrupt:
        return 0
    finally:
        if radar:
            radar.close()
        if not args.once:
            emit(None)
        if sim:
            sim.close()
        if usb:
            usb.close()


if __name__ == '__main__':
    sys.exit(main())
