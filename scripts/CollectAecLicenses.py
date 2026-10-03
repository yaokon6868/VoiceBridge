"""Bundle the exact installed distributions' licenses, including vendored code."""
import importlib.metadata as metadata
from pathlib import Path
import shutil
import sys
target=Path(sys.argv[1]);target.mkdir(parents=True,exist_ok=True)
for name in ('pywebrtc-audio','numpy','sounddevice','cffi','pycparser','pyinstaller'):
    dist=metadata.distribution(name)
    found=0
    for file in dist.files or []:
        if any(word in str(file).lower() for word in ('license','notice','copying')) and dist.locate_file(file).is_file():
            destination=target/name/str(file)
            destination.parent.mkdir(parents=True,exist_ok=True)
            shutil.copy2(dist.locate_file(file),destination);found+=1
    if not found:raise RuntimeError(f'Missing bundled license: {name}')
python_license=Path(sys.base_prefix)/'LICENSE.txt'
if not python_license.exists():raise RuntimeError('Python license missing')
shutil.copy2(python_license,target/'Python-LICENSE.txt')
