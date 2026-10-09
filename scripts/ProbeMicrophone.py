"""Bounded local mic -> AEC -> VB-CABLE test; prints levels, saves no audio.

Does not change any device default, call TTS, or capture system audio.
Run only when the application AEC helper is stopped.
"""
import argparse
import json
from pathlib import Path
import secrets
import subprocess
import sys
import time
from urllib.request import Request, urlopen

import numpy as np
import sounddevice as sd

parser = argparse.ArgumentParser()
parser.add_argument('--mic', required=True)
parser.add_argument('--speaker', required=True)
args = parser.parse_args()
hosts = sd.query_hostapis()
inputs = [(i, d) for i, d in enumerate(sd.query_devices())
          if d['name'] == 'CABLE Output (VB-Audio Virtual Cable)'
          and d['max_input_channels'] > 0
          and hosts[d['hostapi']]['name'] == 'Windows WASAPI']
if len(inputs) != 1:
    raise RuntimeError('One WASAPI CABLE Output capture endpoint is required')
secret = secrets.token_hex(32)
engine = Path(__file__).resolve().parents[1] / 'aec' / 'engine.py'
levels = []
errors = []

def callback(data, frames, timing, flags):
    levels.append(float(np.sqrt(np.mean(data * data))))
    if flags:
        errors.append(str(flags))

with subprocess.Popen([sys.executable, str(engine), '--mic', args.mic,
                       '--speaker', args.speaker, '--port', '17906',
                       '--secret', secret, '--seconds', '9'],
                      stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL) as helper:
    status = None
    request = Request('http://127.0.0.1:17906/status',
                      headers={'X-VoiceBridge-Aec-Token': secret})
    for attempt in range(40):
        if helper.poll() is not None:
            raise RuntimeError('Diagnostic helper exited before readiness')
        try:
            with urlopen(request, timeout=.4) as response:
                status = json.load(response)
            if status.get('state') == 'running' and status.get('callbacks', 0) > 10:
                break
        except OSError:
            pass
        time.sleep(.1)
    else:
        raise RuntimeError('Diagnostic helper did not become ready')
    with sd.InputStream(device=inputs[0][0], channels=1, samplerate=48000,
                        blocksize=480, dtype='float32', callback=callback):
        time.sleep(3)
        with urlopen(request, timeout=.5) as response:
            status = json.load(response)
    helper.wait(timeout=12)

print(json.dumps({'virtualCaptureCallbacks': len(levels),
                  'virtualRmsMedian': float(np.median(levels)),
                  'virtualRmsMax': max(levels, default=0),
                  'physicalMicRms': status.get('micRms'),
                  'cleanMicRms': status.get('cleanRms'),
                  'helperCallbacks': status.get('callbacks'),
                  'captureErrors': len(errors),
                  'helperExitCode': helper.returncode,
                  'audioSaved': False, 'deviceDefaultsChanged': False}))
