import math
import unittest

from scripts.isis_scene import build_scene


class StaticLayoutTests(unittest.TestCase):
    def test_altitude_frame_steps_have_same_vertical_center(self):
        from scripts.isis_scene import tx, ty
        scene=build_scene('reference')
        frame=next(x for x in scene['items'] if x['type']=='line' and len(x['points'])==8
                   and x['points'][0]==[tx(386),ty(170)])
        points=frame['points']
        self.assertLessEqual(abs((points[0][1]+points[-1][1])-(points[3][1]+points[4][1])),1)
    def test_polygons_are_convex_for_lvgl8_software_renderer(self):
        for item in build_scene('reference')['items']:
            if item['type'] != 'polygon':
                continue
            points = item['points']
            cross = []
            for a, b, c in zip(points, points[1:]+points[:1], points[2:]+points[:2]):
                value = (b[0]-a[0])*(c[1]-b[1]) - (b[1]-a[1])*(c[0]-b[0])
                if abs(value) > 2:
                    cross.append(value > 0)
            self.assertLessEqual(len(set(cross)), 1, points)

    def test_square_screen_is_filled_and_text_stays_within_display(self):
        for preset in ('reference', 'acceptance'):
            scene = build_scene(preset)
            x, y, w, h = scene['viewport']
            self.assertGreaterEqual(w, 450)
            self.assertGreaterEqual(h, 440)
            for item in scene['items']:
                if item['type'] != 'text':
                    continue
                x, y, w, h = item['box']
                for px, py in ((x, y), (x+w, y), (x, y+h), (x+w, y+h)):
                    self.assertTrue(0 <= px < 480 and 0 <= py < 480,
                                    (preset, item['text'], px, py))

    def test_level_attitude_has_horizon_on_fixed_aircraft(self):
        scene = build_scene('acceptance')
        self.assertEqual(scene['meta']['horizon_y'], scene['meta']['aircraft_y'])
        center = next(item for item in scene['items']
                      if item['type'] == 'polygon' and item['role'] == 'aircraft_center')
        actual_symbol_y = (min(p[1] for p in center['points']) + max(p[1] for p in center['points'])) / 2
        self.assertLessEqual(abs(scene['meta']['horizon_y']-actual_symbol_y), .5)
        self.assertGreater(build_scene('reference')['meta']['horizon_y'],
                           scene['meta']['aircraft_y'])

    def test_presets_keep_their_values_and_demo_status(self):
        ref = build_scene('reference')
        acceptance = build_scene('acceptance')
        for scene in (ref, acceptance):
            texts = {item['text'] for item in scene['items'] if item['type'] == 'text'}
            self.assertTrue(scene['meta']['demo'])
            self.assertIn('DEMO', texts)
            self.assertIn('1013', texts)
        self.assertEqual(ref['meta']['ias_kt'], 270)
        self.assertEqual(acceptance['meta']['ias_kt'], 250)
        self.assertIn('38000', {x.get('text') for x in ref['items']})
        self.assertIn('10000', {x.get('text') for x in acceptance['items']})
        self.assertNotIn('GPIR3', {x.get('text') for x in acceptance['items']})


if __name__ == '__main__':
    unittest.main()
