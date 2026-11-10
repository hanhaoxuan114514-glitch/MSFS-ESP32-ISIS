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
ble_spec = importlib.util.spec_from_file_location('ble_bridge', Path(bridge.__file__).with_name('ble_bridge.py'))
ble_bridge = importlib.util.module_from_spec(ble_spec)
ble_spec.loader.exec_module(ble_bridge)


class BridgeTests(unittest.TestCase):
    def values(self):
        return [-3, -20, 270, 38000, 360, 1200, 1013.25, -1, .85, 66, -145, 65, -20, 0, 100, 250, 1000, 2, 0, 0, 0, 0, 2, 5, 485,
                1, 50, 100, 160, 2400, 90, 352, 2320, 87, 0, 0, 0, 0, 98, 101, 46, 12,
                4, 11, 5, 2, 224, 225, 99, 39, 5225, 80, 40, 5220, 80, 0, 0, 0, 0, 0, 0, 1.25, 0, 1, 0, 0]

    def test_g_load_supports_signed_values_and_is_optional(self):
        values = self.values()
        values[-5] = -2.34
        self.assertEqual(bridge.normalize(values)['g_load'], -2.34)
        values[-5] = float('nan')
        data = bridge.normalize(values)
        self.assertNotIn('g_load', data)
        self.assertEqual(data['ias_kt'], 270)

    def test_ble_flight_frame_fits_one_write_and_preserves_optional_values(self):
        data = bridge.normalize(self.values())
        data['vertical_speed_fpm'] = -71015
        del data['mach']
        frame = json.loads(bridge.Protocol().line(data))
        packet = ble_bridge.pack(frame)
        self.assertEqual(len(packet), ble_bridge.PACKET_BYTES)
        self.assertLessEqual(len(packet), 244)
        self.assertEqual(struct.unpack_from('<BB16sI', packet),
                         (1, 1, bytes.fromhex(frame['session']), frame['seq']))
        for index, (key, scale) in enumerate(ble_bridge.FIELDS):
            offset = 22 + 3 * index
            fixed = int.from_bytes(packet[offset:offset+3], 'little', signed=True)
            self.assertEqual(fixed, round(frame[key] * scale) if key in frame else -0x800000)
        self.assertIn('IAS=270.0 ALT=38000 HDG=0.0', ble_bridge.status_line(packet))
        disconnected = ble_bridge.pack(json.loads(bridge.Protocol().line()))
        self.assertEqual(ble_bridge.status_line(disconnected), 'BLE telemetry: connected=0 seq=0')

    def test_parking_brake_on_off_and_unavailable(self):
        values = self.values()
        self.assertIs(bridge.normalize(values)['parking_brake'], False)
        self.assertIs(bridge.normalize(values)['wheel_brake'], False)
        values[-4] = -1
        self.assertIs(bridge.normalize(values)['parking_brake'], True)
        values[-3] = 0
        self.assertNotIn('parking_brake', bridge.normalize(values))
        values[-4:-2] = [float('nan'), 1]
        self.assertNotIn('parking_brake', bridge.normalize(values))
        values[-4:] = [0, 1, 100, 16000]
        self.assertIs(bridge.normalize(values)['wheel_brake'], True)
        values[-2:] = [16000, 0]
        self.assertIs(bridge.normalize(values)['wheel_brake'], True)
        values[-2:] = [100, 100]
        self.assertIs(bridge.normalize(values)['wheel_brake'], False)

    def test_engine_count_switches_without_leaking_inactive_engines(self):
        values=self.values();values[17:22]=[4,75,-15,0,80]
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
        self.assertEqual((data['flaps_index'], data['flaps_positions']), (2, 5))
        self.assertEqual(data['tas_kt'], 485)
        values = self.values()
        values[11:17] = [float('nan'), 101, -1, 101, 250, 0]
        data = bridge.normalize(values)
        for name in ('throttle1_pct', 'throttle2_pct', 'reverse1_pct', 'reverse2_pct', 'fuel_pct'):
            self.assertNotIn(name, data)
        self.assertEqual(data['ias_kt'], 270)

    def test_invalid_flap_detent_does_not_drop_flight_sample(self):
        values = self.values()
        values[22:24] = [32, 5]
        data = bridge.normalize(values)
        self.assertNotIn('flaps_index', data)
        self.assertNotIn('flaps_positions', data)
        self.assertEqual(data['ias_kt'], 270)

    def test_flap_count_grows_and_retains_observed_detents(self):
        values = self.values()
        values[22:24] = [5, 5]
        data = bridge.normalize(values)
        self.assertEqual((data['flaps_index'], data['flaps_positions']), (5, 6))
        values[22:24] = [0, 5]
        data = bridge.normalize(values, observed_flap_positions=data['flaps_positions'])
        self.assertEqual((data['flaps_index'], data['flaps_positions']), (0, 6))
        values[23] = 9
        data = bridge.normalize(values, observed_flap_positions=data['flaps_positions'])
        self.assertEqual((data['flaps_index'], data['flaps_positions']), (0, 9))

    def test_speed_bands_and_propeller_classification(self):
        values = self.values()
        values[7] = 0
        jet = bridge.normalize(values)
        self.assertFalse(jet['propeller'])
        self.assertNotIn('prop_rpm', jet)
        self.assertEqual((jet['vs0_kt'], jet['flap_limit_kt'], jet['overspeed_kt']), (50, 100, 160))
        for engine_type in (0, 5):
            values[25] = engine_type
            prop = bridge.normalize(values)
            self.assertTrue(prop['propeller'])
            self.assertEqual(prop['prop_rpm'], 2400)
            self.assertEqual(prop['prop_rpm_pct'], 90)
            self.assertEqual(prop['prop_rpm_2'], 2320)
            self.assertNotIn('prop_rpm_3', prop)
            self.assertEqual(prop['agl_ft'], 352)
        values[26:29] = [100, 50, 0]
        invalid = bridge.normalize(values)
        self.assertEqual(invalid['vs0_kt'], 100)
        self.assertNotIn('flap_limit_kt', invalid)
        self.assertNotIn('overspeed_kt', invalid)

    def test_agl_is_zero_only_when_simulator_confirms_ground_contact(self):
        values = self.values()
        self.assertEqual(bridge.normalize(values)['agl_ft'], 0)
        values[7] = 0
        self.assertEqual(bridge.normalize(values)['agl_ft'], 352)
        values[31] = float('nan')
        self.assertNotIn('agl_ft', bridge.normalize(values))

    def test_fuel_full_empty_and_invalid_capacity(self):
        for quantity, capacity, expected in [(1000,1000,100), (0,1000,0), (1001,1000,None), (-1,1000,None), (1,float('nan'),None)]:
            values=self.values();values[15:17]=[quantity,capacity]
            data=bridge.normalize(values)
            self.assertEqual(data.get('fuel_pct'), expected)

    def test_aircraft_metadata_overrides_generic_engine_type(self):
        for object_class, category, sim_category, expected in [
            ('HELICOPTER', '', '', 2), ('GLIDER', '', '', 3),
            ('LIGHTERTHANAIR', '', 'Airship', 4),
            ('LIGHTERTHANAIR', '', 'HotAirBalloon', 5),
            ('LIGHTERTHANAIR', '', '', 6),
            ('OTHER', 'Tilt Rotor', '', 8),
        ]:
            data = bridge.normalize(self.values(), aircraft_class=object_class,
                                    category=category, sim_category=sim_category)
            self.assertEqual(data['aircraft_kind'], expected)
            self.assertEqual(data['ground_speed_kt'], 98)
            self.assertEqual('rotor_pct' in data, expected in (2, 8))
            if expected == 2:
                self.assertEqual((data['rotor_count'], data['rotor2_rpm'], data['power_2']), (2, 225, 40))
            if expected == 4:
                self.assertEqual((data['prop_rpm'], data['prop_rpm_2']), (5225, 5220))

    def test_units_signs_heading_boolean_and_optional(self):
        data = bridge.normalize(self.values())
        self.assertEqual((data['pitch_deg'], data['bank_deg'], data['heading_deg']), (3, 20, 0))
        self.assertIs(data['on_ground'], True)
        self.assertEqual(data['vertical_speed_fpm'], 1200)

    def test_bad_required_values_reject_entire_sample(self):
        for index, bad in [(0, float('nan')), (1, 181), (2, -1), (6, 0), (7, float('nan'))]:
            values = self.values()
            values[index] = bad
            with self.assertRaises(ValueError):
                bridge.normalize(values)

    def test_vertical_speed_preserves_other_readings(self):
        values = self.values()
        values[5] = -71015
        self.assertEqual(bridge.normalize(values)['vertical_speed_fpm'], -71015)
        for bad in (float('inf'), float('nan'), 150001):
            values[5] = bad
            data = bridge.normalize(values)
            self.assertNotIn('vertical_speed_fpm', data)
            self.assertEqual(data['ias_kt'], values[2])

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

    def test_four_engine_packet_fits_at_maximum_sequence(self):
        values = self.values()
        values[17], values[25] = 4, 0
        for index in (0, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 18, 19, 20, 21):
            values[index] += 0.12345678901234
        protocol = bridge.Protocol()
        protocol.seq = 0xffffffff
        packet = protocol.line(bridge.normalize(values))
        self.assertLessEqual(len(packet) - 1, 1024)
        self.assertEqual(json.loads(packet)['prop_rpm_4'], 0)

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

    def test_native_class_metadata_uses_separate_fixed_strings(self):
        payload = struct.pack('<32s32s32s128s', b'LIGHTERTHANAIR', b'', b'HotAirBalloon', b'Example Balloon')
        header = struct.pack('<10I', 40 + len(payload), 0, 8, 2, 0, 2, 0, 1, 1, 4)
        client = self.client_with_messages([header + payload])
        client.aircraft_title = 'Previous aircraft'
        client.observed_flap_positions = 9
        client.smoothed_aoa = 10
        self.assertIsNone(client.poll())
        self.assertEqual((client.aircraft_class, client.category, client.sim_category),
                         ('LIGHTERTHANAIR', '', 'HotAirBalloon'))
        self.assertEqual(client.aircraft_title, 'Example Balloon')
        self.assertEqual(client.observed_flap_positions, 0)
        self.assertIsNone(client.smoothed_aoa)

    def test_aoa_filter_smooths_then_reinitializes_after_missing_data(self):
        client = self.client_with_messages([])
        client.smoothed_aoa = None
        client.smooth_aoa({'aoa_deg': 0}, 0.05)
        update = {'aoa_deg': 6}
        client.smooth_aoa(update, 0.05)
        self.assertAlmostEqual(update['aoa_deg'], 0.92)
        client.smooth_aoa({}, 0.05)
        update = {'aoa_deg': 6}
        client.smooth_aoa(update, 0.05)
        self.assertEqual(update['aoa_deg'], 6)

    def test_concorde_full_detent_includes_clean_position(self):
        values = self.values()
        values[22:24] = [3, 3]
        result = bridge.normalize(values, aircraft_title='Concorde')
        self.assertEqual((result['flaps_index'], result['flaps_positions']), (3, 4))

    def test_787_nine_positions_and_observed_full_flap_limit(self):
        values = self.values()
        values[22:24] = [8, 9]
        limits = {}
        data = bridge.normalize(values, aircraft_title='787-10', flap_limits=limits)
        self.assertEqual((data['flaps_index'], data['flaps_positions']), (8, 9))
        self.assertEqual(limits, {8: 100})
        values[22], values[27] = 0, -1
        data = bridge.normalize(values, aircraft_title='787-10', flap_limits=limits)
        self.assertEqual(data['flap_limit_kt'], 100)

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
