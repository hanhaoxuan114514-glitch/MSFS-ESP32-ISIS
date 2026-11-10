"""用户截图对应的原创矢量场景；第二阶段只生成明确标注的演示画面。"""
import math

SCALE = 1.25
FONT_SCALE = SCALE / .89
FONT_SIZES = (10, 12, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44)
WHITE = 0xD9DCDA
YELLOW = 0xE6D34C
GREEN = 0x2CF525
CYAN = 0x4DC9E5
BLACK = 0x030609
SKY = 0x2096D5
GROUND = 0xB85948


def tx(x):
    return round(240 + (x - 215) * SCALE)


def ty(y):
    return round(240 + (y - 221) * SCALE)


def build_scene(preset='reference'):
    if preset not in ('reference', 'acceptance'):
        raise ValueError('未知静态演示预设')
    ref = preset == 'reference'
    pitch, ias, altitude, heading = (3.1, 270, 38000, 230) if ref else (0, 250, 10000, 270)
    horizon = 193 + pitch * 5.8
    items = []

    def line(points, color=WHITE, width=2, role=''):
        items.append({'type': 'line', 'points': [[tx(x), ty(y)] for x, y in points],
                      'color': color, 'width': max(1, round(width*FONT_SCALE)), 'role': role})

    def poly(points, color, stroke=None, width=2, role=''):
        pts = [[tx(x), ty(y)] for x, y in points]
        items.append({'type': 'polygon', 'points': pts, 'color': color, 'role': role})
        if stroke is not None:
            line(points + [points[0]], stroke, width, role)

    def rect(x, y, w, h, color=BLACK, stroke=None, width=1, role=''):
        poly([(x, y), (x+w, y), (x+w, y+h), (x, y+h)], color, stroke, width, role)

    def text(value, x, y, w, h, size=22, color=WHITE, align='left', role=''):
        size = min(FONT_SIZES, key=lambda candidate: abs(candidate-size*FONT_SCALE))
        items.append({'type': 'text', 'text': str(value), 'box': [tx(x), ty(y), round(w*SCALE), round(h*SCALE)],
                      'font_size': size, 'color': color, 'align': align, 'role': role})

    # 发光姿态区的上下边缘带曲率，保留参考图的形状。
    outline = [(92, 69), (120, 62), (163, 58), (207, 57), (251, 58), (294, 62),
               (322, 69), (322, 297), (294, 304), (251, 308), (207, 309),
               (163, 308), (120, 304), (92, 297)]
    poly(outline, SKY, role='attitude')
    poly([(92, horizon), (322, horizon), *outline[7:]], GROUND, role='ground')
    line([(92, horizon), (322, horizon)], WHITE, 2, 'horizon')

    for angle in (-15, -12.5, -10, -7.5, -5, -2.5, 2.5, 5, 7.5, 10, 12.5, 15):
        y = horizon - angle * 5.8
        if not 105 <= y <= 288:
            continue
        half = 24 if abs(angle) == 10 else 11 if abs(angle) == 5 else 7
        line([(207-half, y), (207+half, y)], WHITE, 2, 'pitch')
        if abs(angle) == 10:
            text('10', 146, y-14, 30, 28, 20, role='pitch')

    # 横滚弧、刻度及零横滚的上下指针。
    for angles in (range(-30, -5, 3), range(6, 31, 3)):
        arc = [(207 + 120*math.sin(math.radians(a)), 197-120*math.cos(math.radians(a)))
               for a in angles]
        line(arc, WHITE, 2, 'bank')
    for angle in (-30, -20, -10, 10, 20, 30):
        a = math.radians(angle)
        r = 135 if angle in (-30, 0, 30) else 131
        line([(207+120*math.sin(a), 197-120*math.cos(a)),
              (207+r*math.sin(a), 197-r*math.cos(a))], WHITE, 2, 'bank')
    poly([(196, 62), (218, 62), (207, 78)], YELLOW)
    poly([(207, 82), (199, 93), (215, 93)], BLACK, WHITE, 2, 'bank_pointer')
    poly([(199, 96), (215, 96), (218, 100), (196, 100)], BLACK, WHITE, 1, 'bank_pointer')

    # IAS 的白色刻度与固定黄色三角指针。
    for speed in range(ias-50, ias+61, 10):
        y = 195 - (speed-ias)*2.6
        if not 70 <= y <= 294:
            continue
        major = speed % 20 == 10
        line([(82 if major else 85, y), (94, y)], WHITE, 3 if major else 2, 'ias_tick')
        if major:
            text(speed, 43, y-14, 43, 28, 22, role='ias_tick')
    poly([(91, 195), (110, 181), (110, 208)], YELLOW)

    # 气压高度带，外围三位刻度的单位为百英尺，当前读数显示完整英尺。
    for alt in range(altitude-700, altitude+701, 100):
        y = 195 - (alt-altitude)*.168
        if not 75 <= y <= 294:
            continue
        major = alt % 500 == 0
        line([(321, y), (339 if major else 331, y)], WHITE, 3 if major else 2, 'altitude_tick')
        if major and alt != altitude:
            text(f'{alt//100:03d}', 334, y-15, 58, 30, 22, role='altitude_tick')
    # LVGL 8 软件绘制仅支持凸多边形：阶梯框用相交矩形填充。
    rect(279, 178, 107, 34)
    rect(340, 170, 46, 50)
    line([(386, 170), (340, 170), (340, 178), (279, 178), (279, 212),
          (340, 212), (340, 220), (386, 220)], YELLOW, 2)
    text(f'{altitude:d}', 287, 180, 98, 34, 26, GREEN, role='altitude_value')

    # 固定飞机符号；黑心黄边，不能与地平线一起移动。
    rect(138, 187, 37, 7)
    rect(168, 187, 7, 13)
    line([(138, 187), (175, 187), (175, 200), (168, 200), (168, 194), (138, 194), (138, 187)], YELLOW, 2)
    rect(201, 187, 11, 12, BLACK, YELLOW, 2, 'aircraft_center')
    rect(238, 187, 37, 7)
    rect(238, 187, 7, 13)
    line([(238, 187), (275, 187), (275, 194), (245, 194), (245, 200), (238, 200), (238, 187)], YELLOW, 2)

    text('.85' if ref else '--', 59, 313, 47, 29, 22, GREEN, role='mach_demo')
    line([(133, 329), (133, 344), (280, 344), (280, 330)], WHITE, 2)
    for value in range(heading-25, heading+26, 5):
        x = 201 + (value-heading)*2.9
        if not 133 <= x <= 280:
            continue
        v = value % 360
        line([(x, 344), (x, 332 if v % 10 == 0 else 338)], WHITE, 2, 'heading_tick')
        if v % 20 == 0 or v % 90 == 0:
            label = {0: 'N', 90: 'E', 180: 'S', 270: 'W'}.get(v, str(v//10))
            text(label, x-20, 312, 40, 26, 20, align='center', role='heading_tick')
    poly([(201, 325), (194, 341), (208, 341)], GREEN)
    poly([(201, 344), (194, 355), (208, 355)], YELLOW)
    text('1013', 311, 307, 80, 32, 24, CYAN, role='qnh')

    text('DEMO', 47, 44, 68, 20, 12, YELLOW, role='demo_status')

    return {'preset': preset, 'background': BLACK, 'shape': 'square',
            'viewport': [tx(32), ty(42), round(366*SCALE), round(358*SCALE)],
            'meta': {'demo': True, 'pitch_deg': pitch, 'ias_kt': ias,
                     'altitude_ft': altitude, 'heading_deg': heading,
                     'aircraft_y': ty(193), 'horizon_y': ty(horizon)}, 'items': items}
