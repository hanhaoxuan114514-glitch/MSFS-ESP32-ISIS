"""Build a local Blue Marble tile archive from NASA's 21600x10800 global JPEG."""
import argparse
from io import BytesIO
from pathlib import Path
import sys
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'cache/map-build-deps'))
from PIL import Image

TILE_SIZE = 512
LEVELS = (1350, 2700, 5400, 10800, 21600)


def build(source, output):
    temporary = output.with_suffix('.zip.tmp')
    with zipfile.ZipFile(temporary, 'w', compression=zipfile.ZIP_STORED) as archive:
        for width in LEVELS:
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', Image.DecompressionBombWarning)
                Image.MAX_IMAGE_PIXELS = 21600 * 10800
                with Image.open(source) as original:
                    if original.size != (21600, 10800):
                        raise ValueError('expected the 21600×10800 NASA global image')
                    size = (width, width // 2)
                    original.draft('RGB', size)
                    original.load()
                    layer = original if original.size == size else original.resize(size, Image.Resampling.LANCZOS)
                    try:
                        for y in range(0, size[1], TILE_SIZE):
                            for x in range(0, size[0], TILE_SIZE):
                                tile = layer.crop((x, y, min(x+TILE_SIZE, width), min(y+TILE_SIZE, size[1])))
                                with tile, BytesIO() as stream:
                                    tile.save(stream, format='JPEG', quality=86, optimize=True)
                                    archive.writestr(f'{width}/{x//TILE_SIZE}_{y//TILE_SIZE}.jpg', stream.getvalue())
                    finally:
                        if layer is not original:
                            layer.close()
    temporary.replace(output)
    print(f'{output}: {output.stat().st_size:,} bytes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT / 'windows-bridge/map/blue-marble.zip')
    options = parser.parse_args()
    build(options.source, options.output)
