#!/usr/bin/env python3
"""Package committed CarShow plus a privately published Linux license runtime."""
import argparse
import io
import pathlib
import subprocess
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument('runtime', type=pathlib.Path)
parser.add_argument('output', type=pathlib.Path)
args = parser.parse_args()
root = pathlib.Path(__file__).resolve().parents[1]
if not (args.runtime / 'Api').is_file():
    raise SystemExit('Publish the private LicenseServer/Api for linux-x64 first.')
archive = subprocess.check_output(['git', 'archive', '--format=zip', 'HEAD'], cwd=root)
with zipfile.ZipFile(io.BytesIO(archive)) as source, zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED) as target:
    for name in source.namelist():
        data = source.read(name)
        if name == 'Dockerfile':
            data = data.replace(b'COPY --from=build /app/publish .',
                b'COPY --from=build /app/publish .\nCOPY runtime/license-server /app/runtime/license-server\nRUN chmod 700 /app/runtime/license-server/Api\nENV ClinicLicensing__Enabled=true DOTNET_PROCESSOR_COUNT=2')
        target.writestr(name, data)
    for file in args.runtime.rglob('*'):
        if not file.is_file() or file.suffix in {'.pdb', '.db', '.pem', '.cs'}:
            continue
        name = file.relative_to(args.runtime).as_posix()
        data = file.read_bytes()
        if name == 'wwwroot/index.html':
            data = data.replace(b'href="/styles.css"', b'href="/admin/licenses/assets/styles.css"').replace(b'src="/app.js"', b'src="/admin/licenses/assets/app.js"')
        if name == 'wwwroot/app.js':
            data = data.replace(b"'/admin/", b"'/api/license-management/").replace(b'`/admin/', b'`/api/license-management/')
        target.writestr('runtime/license-server/' + name, data)
print(args.output)
