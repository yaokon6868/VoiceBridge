"""Explicit hardware smoke test: silent PCM only, no Fish/API key or recording."""
import argparse
import json
import secrets
import subprocess
import time
from urllib.request import Request, urlopen
from urllib.error import HTTPError, URLError

parser=argparse.ArgumentParser()
parser.add_argument('--engine', required=True)
parser.add_argument('--speaker', required=True)
args=parser.parse_args()
secret=secrets.token_hex(32)
process=subprocess.Popen([args.engine,'--port','17898','--secret',secret,
                          '--speaker',args.speaker,'--seconds','8'],
                         stdout=subprocess.PIPE,stderr=subprocess.PIPE,
                         creationflags=subprocess.CREATE_NO_WINDOW)
def request(path,pcm=None,epoch=0,authorized=True,origin=False):
    headers={'X-Epoch':str(epoch)}
    if authorized:headers['X-VoiceBridge-Aec-Token']=secret
    if origin:headers['Origin']='https://example.test'
    try:
        with urlopen(Request('http://127.0.0.1:17898/'+path,data=pcm,headers=headers),timeout=2) as response:
            return response.status,json.loads(response.read())
    except HTTPError as error:return error.code,{}
try:
    until=time.monotonic()+6
    while True:
        try:
            _,status=request('status')
            if status.get('state')=='running' and status.get('cableAt'):break
        except URLError:pass
        if time.monotonic()>until:raise RuntimeError('Duplex startup did not become ready')
        time.sleep(.1)
    assert request('status',authorized=False)[0]==401
    assert request('status',origin=True)[0]==401
    codes=[request('audio',bytes(32768))[0] for _ in range(25)]
    assert 202 in codes and 429 in codes
    assert request('cancel',b'',epoch=1)[0]==200
    assert request('audio',bytes(882),epoch=0)[0]==410
    _,after=request('status')
    assert after['bufferedBytes']==0 and after['epoch']==1
    assert after['callbacks']>status['callbacks'] and after['cableAt']>=status['cableAt']
    print('PASS: packaged duplex startup, both callbacks live, authenticated IPC, website rejection, bounded backpressure, immediate epoch cancellation')
finally:
    process.terminate()
    process.communicate(timeout=5)
