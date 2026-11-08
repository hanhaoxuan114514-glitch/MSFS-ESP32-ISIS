import importlib.util
import json
import ctypes
import struct
import io
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('bridge', Path(__file__).resolve().parents[1] / 'windows-bridge/bridge.py')
bridge = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bridge)


class BridgeTests(unittest.TestCase):
    def values(self):
        return [-3, -20, 270, 38000, 360, 1200, 1013.25, -1, .85, 66, -145, 65, -20, 0, 100, 250, 1000, 2, 0, 0, 0, 0]

    def test_engine_count_switches_without_leaking_inactive_engines(self):
        values=self.values();values[17:]=[4,75,-15,0,80]
        data=bridge.normalize(values)
        self.assertEqual(data['engine_count'],4)
        self.assertEqual(data['throttle3_pct'],75)
        self.assertEqual(data['reverse4_pct'],80)
        values[17]=2
        data=bridge.normalize(values)
        self.assertEqual(data['engine_count'],2)
        self.assertNotIn('throttle3_pct',data)
        self.assertNotIn('reverse4_pct',data)
        values[17]=2.5
        data=bridge.normalize(values)
        self.assertNotIn('engine_count',data)
        self.assertNotIn('throttle1_pct',data)

    def test_hud_percentages_reverse_and_missing_values(self):
        data = bridge.normalize(self.values())
        self.assertEqual(data['throttle1_pct'], 65)
        self.assertEqual(data['throttle2_pct'], -20)
        self.assertEqual(data['reverse2_pct'], 100)
        self.assertEqual(data['fuel_pct'], 25)
        values = self.values()
        values[11:17] = [float('nan'), 101, -1, 101, 250, 0]
        data = bridge.normalize(values)
        for name in ('throttle1_pct', 'throttle2_pct', 'reverse1_pct', 'reverse2_pct', 'fuel_pct'):
            self.assertNotIn(name, data)
        self.assertEqual(data['ias_kt'], 270)

    def test_fuel_full_empty_and_invalid_capacity(self):
        for quantity, capacity, expected in [(1000,1000,100), (0,1000,0), (1001,1000,None), (-1,1000,None), (1,float('nan'),None)]:
            values=self.values();values[15:17]=[quantity,capacity]
            data=bridge.normalize(values)
            self.assertEqual(data.get('fuel_pct'), expected)

    def test_units_signs_heading_boolean_and_optional(self):
        data = bridge.normalize(self.values())
        self.assertEqual((data['pitch_deg'], data['bank_deg'], data['heading_deg']), (3, 20, 0))
        self.assertIs(data['on_ground'], True)
        self.assertEqual(data['vertical_speed_fpm'], 1200)

    def test_bad_required_values_reject_entire_sample(self):
        for index, bad in [(0, float('nan')), (1, 181), (2, -1), (5, float('inf')), (6, 0), (7, float('nan'))]:
            values = self.values()
            values[index] = bad
            with self.assertRaises(ValueError):
                bridge.normalize(values)

    def test_bad_optional_position_is_removed_together(self):
        values = self.values()
        values[8], values[9] = float('nan'), 91
        data = bridge.normalize(values)
        self.assertNotIn('mach', data)
        self.assertNotIn('latitude_deg', data)
        self.assertNotIn('longitude_deg', data)

    def test_disconnect_and_reconnect_keep_session_and_sequence(self):
        protocol = bridge.Protocol()
        frames = [json.loads(protocol.line(data)) for data in [None, bridge.normalize(self.values()), None, bridge.normalize(self.values())]]
        self.assertEqual([f['seq'] for f in frames], [0, 1, 2, 3])
        self.assertEqual(len({f['session'] for f in frames}), 1)
        self.assertEqual(len(frames[0]), 5)
        self.assertEqual([f['connected'] for f in frames], [False, True, False, True])
        self.assertTrue(all(f['demo'] is False for f in frames))

    def test_framing_and_no_nonfinite_json(self):
        line = bridge.Protocol().line(bridge.normalize(self.values()))
        self.assertTrue(line.endswith(b'\n'))
        self.assertLessEqual(len(line) - 1, 1024)
        line.decode('ascii')
        with self.assertRaises(ValueError):
            bridge.Protocol().line({'mach': float('nan')})

    def test_sequence_never_wraps(self):
        protocol = bridge.Protocol()
        protocol.seq = 0xffffffff
        self.assertEqual(json.loads(protocol.line())['seq'], 0xffffffff)
        with self.assertRaises(OverflowError):
            protocol.line()

    def client_with_messages(self, messages):
        class FakeDLL:
            def __init__(self):
                self.buffers = [ctypes.create_string_buffer(raw) for raw in messages]

            def SimConnect_GetNextDispatch(self, handle, pointer, size):
                if not self.buffers:
                    return -2147467259
                self.current = self.buffers.pop(0)
                ctypes.cast(pointer, ctypes.POINTER(ctypes.c_void_p))[0] = ctypes.addressof(self.current)
                ctypes.cast(size, ctypes.POINTER(ctypes.c_uint32))[0] = len(self.current) - 1
                return 0
        client = bridge.SimConnect.__new__(bridge.SimConnect)
        client.dll, client.handle = FakeDLL(), ctypes.c_void_p(1)
        return client

    def test_native_packed_layout_and_open_event(self):
        payload = struct.pack('<'+'d'*len(self.values()), *self.values())
        raw = struct.pack('<10I', 40 + len(payload), 0, 8, 1, 0, 1, 0, 1, 1, len(self.values())) + payload
        client = self.client_with_messages([struct.pack('<3I', 12, 0, 2), raw])
        self.assertEqual(client.poll(), tuple(self.values()))
        self.assertIsNone(client.poll())  # empty dispatch E_FAIL is not fatal

    def test_native_quit_and_truncated_data_reject(self):
        for raw in [struct.pack('<3I', 12, 0, 3), struct.pack('<3I', 12, 0, 8), struct.pack('<3I', 12, 0, 1)]:
            with self.assertRaises(OSError):
                self.client_with_messages([raw]).poll()

    def test_usb_open_sets_control_lines_before_open(self):
        events = []
        class Device:
            def __setattr__(self, name, value):
                events.append((name, value))
            def open(self):
                events.append('open')
            def close(self):
                events.append('close')
        class SerialModule:
            @staticmethod
            def Serial(**kwargs):
                events.append(kwargs)
                return Device()
        bridge.open_usb(SerialModule, 'COM_FAKE', 115200)
        self.assertIsNone(events[0]['port'])
        self.assertEqual(events[0]['timeout'], 0)
        self.assertEqual(events[1:], [('dtr', False), ('rts', False), ('port', 'COM_FAKE'), 'open'])

    def test_device_lines_are_bounded_and_only_on_stderr(self):
        log = bridge.DeviceLog()
        stderr, stdout = io.StringIO(), io.StringIO()
        with redirect_stderr(stderr), redirect_stdout(stdout):
            log.feed(b'first\r\npart')
            log.feed(b'ial\n' + b'x' * 4096)
            self.assertLessEqual(len(log.pending), 2048)
            log.feed(b'ignored\nlast\n')
            log.feed(b'old connection')
            log.clear()
            log.feed(b'new connection\n')
        self.assertEqual(stdout.getvalue(), '')
        self.assertEqual(stderr.getvalue(), '[device] first\n[device] partial\n[device] last\n[device] new connection\n')

    def test_device_drain_reads_available_bytes_without_wait(self):
        class Device:
            chunks = [b'one\n', b'two\n']
            @property
            def in_waiting(self):
                return len(self.chunks[0]) if self.chunks else 0
            def read(self, count):
                chunk = self.chunks.pop(0)
                self.asserted_count = count
                return chunk
        output = io.StringIO()
        with redirect_stderr(output):
            bridge.DeviceLog().drain(Device())
        self.assertEqual(output.getvalue(), '[device] one\n[device] two\n')


if __name__ == '__main__':
    unittest.main()
