from pathlib import Path
import sys,zipfile,json,hashlib
archive=Path(sys.argv[1]);expected=Path(sys.argv[2]).read_text().strip()
with zipfile.ZipFile(archive) as z:
 manifest=json.loads(z.read('SOURCE-MANIFEST.json'))
 if manifest['build']!=expected or z.read('BUILD.txt').decode().strip()!=expected:raise RuntimeError('Source/binary build identity mismatch')
 for name,digest in manifest['files'].items():
  if name.startswith('/') or '..' in Path(name).parts:raise RuntimeError('Unsafe source path')
  if hashlib.sha256(z.read(name)).hexdigest()!=digest:raise RuntimeError('Source hash mismatch: '+name)
 if not manifest['files']:raise RuntimeError('Empty source manifest')
print('PASS matching source archive build identity and every source-input hash')
