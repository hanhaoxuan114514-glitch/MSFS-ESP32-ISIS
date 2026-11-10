import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('radar_feed', Path(__file__).resolve().parents[1] /
                                              'windows-bridge/radar_feed.py')
radar = importlib.util.module_from_spec(spec)
spec.loader.exec_module(radar)


class RadarTests(unittest.TestCase):
    def test_targets_are_filtered_and_project_across_antimeridian(self):
        site = (0, 179.95, 25, 'TEST')
        aircraft = radar.targets_for_site(site, [
            {'lat': 0, 'lon': -179.95, 'flight': ' TEST123 ', 'alt_baro': 32000, 'track': 90},
            {'lat': 0, 'lon': 179.96, 'alt_baro': 'ground'},
            {'lat': 0, 'lon': 179.97, 'seen_pos': 45},
            {'lat': 0, 'lon': 20},
        ])
        self.assertEqual(len(aircraft), 1)
        target = aircraft[0]
        self.assertEqual(target['callsign'], 'TEST123')
        self.assertAlmostEqual(target['distance'], 6, places=1)
        self.assertEqual(target['north'], 0)
        self.assertGreater(target['east'], 0)

    def test_radar_packet_fits_existing_ble_single_write(self):
        site = (0, 0, 25, 'TEST')
        targets = radar.targets_for_site(site, [
            {'lat': i*.01, 'lon': .01, 'flight': 'EX123', 'alt_baro': None, 'track': 270}
            for i in range(20)])
        packet = radar.pack_radar(2, site, targets)
        self.assertEqual(len(packet), 184)
        self.assertEqual(struct.unpack_from('<4B12s', packet)[:4], (2, 2, 25, 12))
        self.assertEqual(struct.unpack_from('<hhBh7s', packet, 16)[2:4], (192, -32768))
        self.assertEqual(radar.pack_radar(0)[:4], bytes((2, 0, 0, 0)))

    def test_saved_location_can_be_disabled_without_losing_coordinates(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'radar-settings.json'
            site = dict(enabled=True, lat=52.3, lon=4.7, radius=25, name='EHAM')
            path.write_text(json.dumps(site), encoding='utf-8')
            self.assertEqual(radar.read_site(path), (52.3, 4.7, 25, 'EHAM'))
            site['enabled'] = False
            path.write_text(json.dumps(site), encoding='utf-8')
            self.assertIsNone(radar.read_site(path))
            self.assertEqual(json.loads(path.read_text())['lat'], 52.3)
