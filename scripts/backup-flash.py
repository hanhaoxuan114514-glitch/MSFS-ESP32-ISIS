"""以小块读取现有 Flash，降低内存占用；仅备份，不写入设备。"""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from esptool.cmds import detect_chip


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--port', default='COM6')
    args = parser.parse_args()
    backup_dir = Path(__file__).resolve().parent.parent / 'backups'
    backup_dir.mkdir(exist_ok=True)
    final = backup_dir / 'pre-project-16mb.bin'
    partial = backup_dir / 'pre-project-16mb.bin.partial'
    if final.exists():
        raise SystemExit('完整备份已存在，拒绝覆盖。')
    size = 16 * 1024 * 1024
    block_size = 256 * 1024
    digest = hashlib.sha256()
    loader = detect_chip(port=args.port, baud=115200)
    try:
        if loader.CHIP_NAME != 'ESP32-S3':
            raise RuntimeError('连接设备不是 ESP32-S3，停止读取。')
        if not loader.IS_STUB:
            loader = loader.run_stub()
        loader.flash_spi_attach(0)
        if (loader.flash_id() >> 16) & 0xFF != 0x18:
            raise RuntimeError('实际 Flash 容量标识不符合 16 MB，停止读取。')
        loader.flash_set_parameters(size)
        loader.change_baud(460800)
        with partial.open('wb') as output:
            for offset in range(0, size, block_size):
                # esptool read_flash 对每块验证设备返回的 MD5 摘要。
                data = loader.read_flash(offset, block_size)
                if len(data) != block_size:
                    raise RuntimeError('读取块长度不符，保留 partial 供诊断。')
                output.write(data)
                output.flush()
                digest.update(data)
                print(f'已读取并校验 {offset + block_size:,} / {size:,} 字节', flush=True)
        if partial.stat().st_size != size:
            raise RuntimeError('完整长度不符，不生成正式备份。')
        # 再读磁盘文件验证保存结果，避免只校验内存中的内容。
        with partial.open('rb') as saved:
            disk_digest = hashlib.file_digest(saved, 'sha256').hexdigest()
        if disk_digest != digest.hexdigest():
            raise RuntimeError('磁盘备份校验不符。')
        partial.rename(final)
        final.with_suffix('.bin.sha256').write_text(
            f'{disk_digest}  {final.name}\n', encoding='ascii'
        )
        print(f'完整备份成功：{size:,} 字节；SHA256 {disk_digest}', flush=True)
    finally:
        try:
            loader.hard_reset()
        finally:
            loader._port.close()


if __name__ == '__main__':
    main()
