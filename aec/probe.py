"""Opt-in local acoustic measurement; retains metrics, never microphone audio."""
import argparse
import json
import time
from types import SimpleNamespace
import numpy as np
import sounddevice as sd
from engine import PlaybackQueue, DuplexProcessor, select_devices, RATE, FRAME


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--mic', default='')
    parser.add_argument('--speaker', required=True)
    parser.add_argument('--seconds', type=int, default=10)
    parser.add_argument('--pcm', default='')
    parser.add_argument('--detect-barge', action='store_true')
    args = parser.parse_args()
    mic, speaker, cable = select_devices(args)
    q = PlaybackQueue(capacity=44100 * 2 * (args.seconds + 1))
    rng = np.random.default_rng(312)
    far = rng.normal(0, .035, 44100 * args.seconds).astype(np.float32)
    far = np.convolve(far, np.ones(4)/4, mode='same')
    pcm = np.clip(far * 32767, -32768, 32767).astype('<i2').tobytes()
    if args.pcm:
        from pathlib import Path
        pcm=Path(args.pcm).read_bytes()
        args.seconds=len(pcm)/88200
        q=PlaybackQueue(capacity=len(pcm)+88200)
    for start in range(0, len(pcm), 32768):
        assert q.push(0, pcm[start:start+32768]) == 202
    dsp = DuplexProcessor(q, detect_barge=args.detect_barge)
    stats = []
    def callback(indata, outdata, frames, timing, flags):
        start = time.perf_counter()
        dsp.callback(indata, outdata, frames, timing, flags)
        stats.append((dsp.status['micRms'], dsp.status['cleanRms'],
                      (time.perf_counter()-start)*1000, dsp.echo_evidence.delay_ms, dsp.echo_evidence.similarity))
    with sd.OutputStream(device=cable['index'], channels=2, samplerate=RATE,
                         blocksize=FRAME, dtype='float32', latency=.04, callback=dsp.cable_callback), \
         sd.Stream(device=(mic['index'], speaker['index']), channels=(1, 2),
                   samplerate=RATE, blocksize=FRAME, dtype='float32', latency=.04,
                   callback=callback) as stream:
        dsp.device_delay_ms=sum(stream.latency)*1000
        time.sleep(args.seconds)
    tail = np.asarray(stats)[-300:]
    reduction = 10*np.log10((np.mean(tail[:, 0]**2)+1e-12)/(np.mean(tail[:, 1]**2)+1e-12))
    print(json.dumps({'physicalMic':mic['name'], 'speaker':speaker['name'],
        'observedReductionDb':round(float(reduction),2), 'callbacks':len(stats),
        'callbackP95Ms':round(float(np.percentile(np.asarray(stats)[:, 2],95)),2),
        'echoDelayMedianMs':round(float(np.median(tail[:,3])),1),
        'echoSimilarityMedian':round(float(np.median(tail[:,4])),3),
        'status':dsp.status}, ensure_ascii=True))


if __name__ == '__main__':
    main()
