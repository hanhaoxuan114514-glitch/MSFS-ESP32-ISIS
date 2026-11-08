"""从共享场景生成 LVGL 8 绘图表与 PC 预览；不使用用户截图作屏幕纹理。"""
from pathlib import Path
import json
import sys

from isis_scene import build_scene

ROOT = Path(__file__).resolve().parent.parent
FONT = ROOT / 'esp32-firmware/managed_components/lvgl__lvgl/scripts/built_in_font/Montserrat-Medium.ttf'
SIZES = (10, 12, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44)


def color(value):
    return ((value >> 16) & 255, (value >> 8) & 255, value & 255)


def fit_text(scene):
    from PIL import ImageFont
    for item in scene['items']:
        if item['type'] != 'text':
            continue
        width = item['box'][2]
        while True:
            font = ImageFont.truetype(str(FONT), item['font_size'])
            if font.getlength(item['text']) <= width:
                break
            smaller = [s for s in SIZES if s < item['font_size']]
            if not smaller:
                raise ValueError(f'文字超过可用范围：{item["text"]}')
            item['font_size'] = max(smaller)


def render(scene, path):
    from PIL import Image, ImageDraw, ImageFont
    scale = 3
    image = Image.new('RGB', (480*scale, 480*scale), color(scene['background']))
    draw = ImageDraw.Draw(image)
    for item in scene['items']:
        fill = color(item['color'])
        if item['type'] == 'polygon':
            draw.polygon([(x*scale, y*scale) for x, y in item['points']], fill=fill)
        elif item['type'] == 'line':
            draw.line([(x*scale, y*scale) for x, y in item['points']], fill=fill,
                      width=item['width']*scale, joint='curve')
        else:
            x, y, w, h = item['box']
            font = ImageFont.truetype(str(FONT), item['font_size']*scale)
            if item['align'] == 'center':
                x += (w-font.getlength(item['text'])/scale)/2
            draw.text((x*scale, y*scale), item['text'], fill=fill, font=font, anchor='lt')
    image.resize((480, 480), Image.Resampling.LANCZOS).save(path)


def header(scene):
    roles = ['static'] + sorted({item['role'] for item in scene['items'] if item['role']})
    parts = ['/* 由 scripts/generate-isis-ui.py 生成。所有数值均为 DEMO。 */',
             '#pragma once', '#include "lvgl.h"',
             'enum { ' + ','.join('ISIS_ROLE_'+role.upper() for role in roles) + ' };',
             'typedef struct { unsigned type, role; uint32_t color; unsigned width, count, font_size, center;',
             'const lv_point_t *points; lv_area_t box; const char *text; } isis_command_t;']
    for i, item in enumerate(scene['items']):
        if item['type'] != 'text':
            coords = ','.join('{%d,%d}' % tuple(p) for p in item['points'])
            parts.append(f'static const lv_point_t isis_points_{i}[] = {{{coords}}};')
    parts.append('static const isis_command_t isis_commands[] = {')
    for i, item in enumerate(scene['items']):
        common = f'.color=0x{item["color"]:06x},.role=ISIS_ROLE_{(item["role"] or "static").upper()}'
        if item['type'] == 'text':
            x, y, w, h = item['box']
            parts.append('{.type=2,' + common + f',.font_size={item["font_size"]},'
                         f'.center={int(item["align"]=="center")},.box={{{x},{y},{x+w-1},{y+h-1}}},'
                         f'.text={json.dumps(item["text"])}}},')
        else:
            kind = 0 if item['type'] == 'polygon' else 1
            parts.append(f'{{.type={kind},{common},.width={item.get("width", 0)},'
                         f'.count={len(item["points"])},.points=isis_points_{i}}},')
    parts += ['};', f'#define ISIS_BACKGROUND 0x{scene["background"]:06x}',
              '#define ISIS_COMMAND_COUNT (sizeof(isis_commands)/sizeof(isis_commands[0]))']
    return '\n'.join(parts)+'\n'


def main():
    if '--header-only' in sys.argv:
        # 实时固件自行绘制文本；更新静态矢量框线不需要图像库。
        (ROOT/'esp32-firmware/main/ui_scene.generated.h').write_text(header(build_scene('reference')), encoding='utf-8')
        print('Static vector header generated (no preview).')
        return
    output = ROOT/'assets'
    output.mkdir(exist_ok=True)
    for preset in ('reference', 'acceptance'):
        scene = build_scene(preset)
        fit_text(scene)
        render(scene, output/f'isis-{preset}-preview.png')
        (output/f'isis-{preset}-scene.json').write_text(
            json.dumps(scene, ensure_ascii=False, indent=2), encoding='utf-8')
        if preset == 'reference':
            (ROOT/'esp32-firmware/main/ui_scene.generated.h').write_text(header(scene), encoding='utf-8')
        print(f'{preset}: {len(scene["items"])} drawing commands')


if __name__ == '__main__':
    main()
