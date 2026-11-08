"""Relocate generated build paths from the former workspace alias W: to V:."""
from pathlib import Path
import shutil
root=Path(__file__).resolve().parent.parent
build=root/'esp32-firmware/build-s3'
backup=root/'artifacts/pre-hud'
backup.mkdir(parents=True,exist_ok=True)
for name in ('lvgl_demo_v8.bin','flash_args'):
    p=build/name
    if p.exists() and not (backup/name).exists(): shutil.copy2(p,backup/name)
count=0
for p in build.rglob('*'):
    if p.is_file() and (p.suffix in ('.txt','.cmake','.ninja','.json','.d','.rsp','.cfg') or p.name in ('.ninja_deps','.ninja_log')):
        data=p.read_bytes();new=data.replace(b'W:/',b'V:/').replace(b'W:\\',b'V:\\').replace(b'w:/',b'V:/').replace(b'w:\\',b'V:\\')
        if new!=data:p.write_bytes(new);count+=1
p=root/'tools/espressif/python_env/idf5.3_py3.11_env/pyvenv.cfg'
data=p.read_text();p.write_text(data.replace('W:\\','V:\\'))
print('Relocated generated build files:',count)
