#!/usr/bin/env python3
"""End-to-end checks against the built CarShow and packaged private server; no live data."""
import base64
import datetime
import http.client
import http.cookies
import json
import os
import pathlib
import shutil
import subprocess
import sys
import tempfile
import time
import zipfile
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding

root = pathlib.Path(__file__).resolve().parents[1]
work = pathlib.Path(tempfile.mkdtemp(prefix='clinic-e2e-'))
data = pathlib.Path(tempfile.mkdtemp(prefix='clinic-e2e-', dir='/dev/shm'))
cookies = {}
csrf = None
process = None
log = open(work / 'server.log', 'w')

def request(path, method='GET', body=None, origin='https://test.local', extra=None, authenticated=True):
    headers = {'Host': 'test.local', 'X-Forwarded-Proto': 'https'}
    if origin is not None: headers['Origin'] = origin
    if authenticated:
        headers['Cookie'] = '; '.join(k + '=' + v for k, v in cookies.items())
        if csrf: headers['X-CSRF-TOKEN'] = csrf
    if extra: headers.update(extra)
    encoded = json.dumps(body) if body is not None else None
    if encoded is not None: headers['Content-Type'] = 'application/json'
    connection = http.client.HTTPConnection('127.0.0.1', 5500, timeout=5)
    connection.request(method, path, encoded, headers)
    response = connection.getresponse()
    payload = response.read().decode()
    for key, value in response.getheaders():
        if key.lower() == 'set-cookie' and authenticated:
            parsed = http.cookies.SimpleCookie(value)
            cookies.update({k: v.value for k, v in parsed.items()})
    status = response.status
    connection.close()
    try: payload = json.loads(payload)
    except ValueError: pass
    return status, payload

try:
    with zipfile.ZipFile(sys.argv[2]) as bundle:
        for name in bundle.namelist():
            if name.startswith('runtime/license-server/'): bundle.extract(name, work)
    runtime = work / 'runtime/license-server'
    (runtime / 'Api').chmod(0o700)
    env = dict(os.environ, DOTNET_PROCESSOR_COUNT='2', PORT='5500',
        ClinicLicensing__Enabled='true', ClinicLicensing__PublicOrigin='https://test.local',
        ClinicLicensing__DataPath=str(data), ClinicLicensing__RuntimePath=str(runtime),
        JwtSettings__SecretKey='only-for-isolated-test-000000000000000000000000', Crawler__Enabled='false')
    process = subprocess.Popen([sys.argv[1], str(root / 'CarShow/bin/Release/net10.0/CarShow.dll')],
        cwd=root / 'CarShow', env=env, stdout=log, stderr=log)
    for attempt in range(100):
        try:
            if request('/admin/licenses')[0] == 200: break
        except (ConnectionError, OSError): pass
        time.sleep(.1)
    else: raise AssertionError('Server did not become ready')
    assert request('/health/live')[0] == 200
    assert 'مدیریت لایسنس کلینیک' in request('/admin/licenses')[1]
    assert '/api/license-management/' in request('/admin/licenses/assets/app.js')[1]
    assert request('/api/license-management/licenses', authenticated=False)[0] == 401
    assert request('/api/license-management/login', 'POST', {}, origin='https://evil.invalid')[0] == 403
    assert request('/api/license-management/login', 'POST', {}, authenticated=False)[0] == 400
    assert request('/api/license-management/unknown')[0] == 404
    assert request('/admin/licenses', extra={'Host':'evil.invalid'})[0] == 400
    csrf = request('/api/license-management/csrf')[1]['token']
    assert request('/api/license-management/login', 'POST', {'key': (data / 'admin-key').read_text()})[0] == 204
    csrf = request('/api/license-management/csrf')[1]['token']
    expires = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(days=2)).isoformat()
    status, issued = request('/api/license-management/licenses', 'POST', {
        'clinicName': 'Isolated test', 'productId':'dental-clinic', 'environment':'Production', 'expiresAt':expires})
    assert status == 200
    license = issued['license']
    status, signing = request('/api/license-management/signing-key')
    public = serialization.load_pem_public_key(signing['publicKey'].encode())
    def check(active):
        status, signed = request('/api/licenses/check', 'POST', {
            'installationId':license['installationId'], 'productId':'dental-clinic', 'environment':'Production'},
            origin=None, extra={'X-Installation-Secret':issued['installationSecret']}, authenticated=False)
        assert status == 200
        public.verify(base64.b64decode(signed['signature']), (signed['keyId']+'\n'+signed['payload']).encode(),
            padding.PSS(mgf=padding.MGF1(hashes.SHA256()), salt_length=32), hashes.SHA256())
        assert json.loads(signed['payload'])['IsActive'] is active
    check(True)
    for active in [False, True]:
        status, license = request('/api/license-management/licenses/'+license['id'], 'PUT', {
            'isActive':active, 'expiresAt':expires, 'revision':license['revision']})
        assert status == 200
        check(active)
    assert request('/api/license-management/licenses/'+license['id'], 'PUT', {
        'isActive':False, 'expiresAt':expires, 'revision':0})[0] == 409
    assert request('/api/license-management/logout', 'POST', {})[0] == 204
    assert request('/api/license-management/licenses')[0] == 401
    print('PASS: health, UI/assets, authentication, CSRF, origin/host checks, route allowlist, create, signed active/disabled/reactivated checks, revision conflict, logout')
finally:
    if process:
        process.terminate()
        try: process.wait(timeout=10)
        except subprocess.TimeoutExpired: process.kill(); process.wait()
    log.close()
    shutil.rmtree(work)
    shutil.rmtree(data)
