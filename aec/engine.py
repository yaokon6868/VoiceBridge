"""VoiceBridge-owned playback + physical microphone AEC. Never system loopback.

The duplex callback renders Fish PCM and supplies that exact frame to WebRTC,
with the device's capture/render timestamps. Only cleaned mic goes to VB-CABLE.
All audio is transient memory. No recordings, network TTS, or device defaults.
"""
import argparse
from collections import deque
import ctypes
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
import secrets
import threading
import time

import numpy as np
import sounddevice as sd
from pywebrtc_audio import AudioProcessor, VoiceDetector

RATE, FRAME, FISH_FRAME = 48000, 480, 441


class PlaybackQueue:
    def __init__(self, capacity=44100 * 2 * 8):
        self.lock = threading.Lock()
        self.capacity = capacity
        self.data = bytearray()
        self.epoch = -1
        self.accepted = 0

    def cancel(self, epoch):
        with self.lock:
            if epoch < self.epoch:
                return False
            self.epoch = epoch
            self.data.clear()
            return True

    def push(self, epoch, pcm):
        if len(pcm) % 2 or len(pcm) > 32768:
            return 400
        with self.lock:
            if epoch < self.epoch:
                return 410
            if epoch > self.epoch:
                self.epoch = epoch
                self.data.clear()
            if len(self.data) + len(pcm) > self.capacity:
                return 429
            self.data.extend(pcm)
            self.accepted += len(pcm)
            return 202

    def read(self):
        with self.lock:
            count = min(len(self.data), FISH_FRAME * 2)
            pcm = bytes(self.data[:count])
            del self.data[:count]
        samples = np.zeros(FISH_FRAME, np.float32)
        samples[:count // 2] = np.frombuffer(pcm, '<i2').astype(np.float32) / 32768
        # Exact 10-ms rate ratio. Boundary continuity is maintained by the
        # 441-sample FIFO; interpolation affects playback and reference equally.
        return np.interp(np.arange(FRAME) * 441 / 480,
                         np.arange(FISH_FRAME), samples).astype(np.float32)

    def snapshot(self):
        with self.lock:
            return {'epoch': self.epoch, 'bufferedBytes': len(self.data),
                    'receivedBytes': self.accepted}


class CaptureFifo:
    """Small elastic FIFO compensates separate virtual-output clock drift."""
    def __init__(self):
        self.lock = threading.Lock()
        self.data = np.zeros(FRAME * 2, np.float32)
        self.underruns = 0
        self.overruns = 0

    def push(self, frame):
        with self.lock:
            self.data = np.concatenate((self.data, frame))
            if len(self.data) > FRAME * 12:
                self.data = self.data[-FRAME * 6:]
                self.overruns += 1

    def read(self):
        with self.lock:
            if len(self.data) < FRAME + 1:
                self.underruns += 1
                return np.zeros(FRAME, np.float32)
            # Gradual +/- 0.2% rate correction, rather than dropping speech frames.
            consume = int(np.clip(FRAME + (len(self.data) - FRAME * 3) / 1000,
                                  FRAME - 1, FRAME + 1))
            result = np.interp(np.arange(FRAME) * consume / FRAME,
                               np.arange(consume + 1), self.data[:consume + 1])
            self.data = self.data[consume:]
            return result.astype(np.float32)


class LinearEchoPath:
    """Partitioned frequency-domain NLMS, 120ms tail after device delay.

    The adaptive linear path runs before WebRTC's residual echo control. It
    subtracts an estimated echo, never attenuates the entire microphone signal.
    """
    def __init__(self, partitions=12):
        self.partitions = partitions
        self.weights = np.zeros((partitions, FRAME + 1), np.complex128)
        self.history = np.zeros_like(self.weights)
        self.render = deque(maxlen=60)
        self.previous = np.zeros(FRAME, np.float32)
        self.power = np.ones(FRAME + 1) * .01
        self.trained = 0
        self.delay_frames = None

    def process(self, near, far, delay_ms):
        self.render.appendleft(far.copy())
        delay_frames = max(0, int(delay_ms // 10) - 2)
        if self.delay_frames is None or abs(delay_frames-self.delay_frames)>=2:
            self.delay_frames=delay_frames
            self.weights.fill(0);self.history.fill(0);self.previous.fill(0)
            self.power.fill(.01);self.trained=0
        delay_frames=self.delay_frames
        delayed = self.render[delay_frames] if len(self.render) > delay_frames else np.zeros(FRAME, np.float32)
        x = np.fft.rfft(np.r_[self.previous, delayed])
        self.previous = delayed.copy()
        self.history[1:] = self.history[:-1].copy()
        self.history[0] = x
        estimated = np.fft.irfft(np.sum(self.weights * self.history, axis=0), n=FRAME * 2)[FRAME:]
        residual = near - estimated
        far_power = float(np.mean(delayed ** 2))
        echo_power = float(np.mean(estimated ** 2))
        near_power = float(np.mean(near ** 2))
        double_talk = self.trained > 200 and echo_power > 1e-7 and near_power > echo_power * 3
        if far_power > 1e-7 and not double_talk:
            spectrum = np.fft.rfft(np.r_[np.zeros(FRAME), residual])
            self.power = .8 * self.power + .2 * np.sum(abs(self.history) ** 2, axis=0)
            gradient = .5 * self.history.conj() * spectrum / (self.power + .001)
            impulse = np.fft.irfft(gradient, n=FRAME * 2, axis=1)
            impulse[:, FRAME:] = 0
            self.weights += np.fft.rfft(impulse, axis=1)
            self.trained += 1
        return residual.astype(np.float32)


class DuplexProcessor:
    def __init__(self, playback, detect_barge=True):
        self.playback = playback
        self.apm = AudioProcessor(sample_rate=RATE, echo_cancellation=True,
                                  noise_suppression=False, auto_gain_control=False)
        self.linear = LinearEchoPath()
        self.vad = VoiceDetector(sample_rate=RATE)
        self.fifo = CaptureFifo()
        self.status = {'state': 'starting', 'callbacks': 0, 'bargeIns': 0,
                       'inputErrors': 0, 'outputErrors': 0}
        self.streak = 0
        self.last_barge = 0
        self.last_render = 0
        self.echo_evidence = EchoEvidence()
        self.residual_evidence = EchoEvidence()
        self.detect_barge = detect_barge
        self.device_delay_ms = 0
        self.delay_candidates = deque(maxlen=7)
        self.estimated_delay_ms = None

    def process(self, near, far, delay_ms):
        self.apm.stream_delay_ms = int(np.clip(delay_ms, 0, 500))
        residual = self.linear.process(np.asarray(near, np.float32), np.asarray(far, np.float32), delay_ms)
        independent = self.residual_evidence.update(residual, far) < .25
        clean = self.apm.process(residual,
                                 np.asarray(far, np.float32)).astype(np.float32)
        # AEC3 residual suppression can classify independent near-end energy as
        # echo after a far-only period. Keep the linear residual when it contains
        # substantial independent energy; never gate speech to obtain a better
        # echo-only score. Double-talk preservation is an acceptance condition.
        raw_power = float(np.mean(np.asarray(near) ** 2))
        if independent and float(np.mean(residual ** 2)) > raw_power * .25:
            clean = residual
        return clean

    def callback(self, indata, outdata, frames, timing, flags):
        if frames != FRAME:
            outdata.fill(0)
            raise sd.CallbackAbort('Unexpected duplex frame size')
        far = self.playback.read()
        # These timestamps share the duplex stream's clock. They include the
        # device buffering, not Python IPC/network arrival timing.
        delay = max(0, (timing.outputBufferDacTime - timing.inputBufferAdcTime) * 1000)
        if not timing.outputBufferDacTime or not timing.inputBufferAdcTime:
            delay = self.device_delay_ms
        near = indata[:, 0]
        similarity = self.echo_evidence.update(near, far)
        if similarity > .3 and self.echo_evidence.frames % 4 == 0:
            self.delay_candidates.append(self.echo_evidence.delay_ms)
            if len(self.delay_candidates)==7 and max(self.delay_candidates)-min(self.delay_candidates)<15:
                self.estimated_delay_ms=float(np.median(self.delay_candidates))
        if self.estimated_delay_ms is not None:
            delay=self.estimated_delay_ms
        clean = self.process(near, far, delay)
        outdata[:] = far[:, None]
        self.fifo.push(clean)
        now = time.monotonic()
        raw_rms, clean_rms, far_rms = [float(np.sqrt(np.mean(x * x))) for x in (near, clean, far)]
        if far_rms > .001:
            self.last_render = now
        probability = float(self.vad.process(clean))
        # Never gate or mute the mic. Barge-in only cancels our playback queue.
        speaking = probability > .8 and clean_rms > .006 and clean_rms > raw_rms * .35 and similarity < .65 and self.residual_evidence.similarity < .25
        self.streak = self.streak + 1 if speaking else 0
        if self.detect_barge and now - self.last_render < .5 and self.streak >= 8 and now - self.last_barge > .8:
            snapshot = self.playback.snapshot()
            self.playback.cancel(snapshot['epoch'] + 1)
            self.status['bargeIns'] += 1
            self.last_barge = now
        self.status.update(callbacks=self.status['callbacks'] + 1,
                           delayMs=round(delay, 1), micRms=round(raw_rms, 6),
                           cleanRms=round(clean_rms, 6), renderRms=round(far_rms, 6),
                           speechProbability=round(probability, 3), echoSimilarity=round(similarity, 3),
                           residualSimilarity=round(self.residual_evidence.similarity,3), at=time.time())
        if flags:
            self.status['inputErrors'] += int(bool(flags.input_overflow))
            self.status['outputErrors'] += int(bool(flags.output_underflow))

    def cable_callback(self, outdata, frames, timing, flags):
        if frames != FRAME:
            outdata.fill(0)
            raise sd.CallbackAbort('Unexpected virtual output frame size')
        outdata[:] = self.fifo.read()[:, None]
        self.status.update(cableAt=time.time(), cableUnderruns=self.fifo.underruns, cableOverruns=self.fifo.overruns)


class EchoEvidence:
    """Identify correlated speaker output during cold start, without mic gating.

    Correlation is only used to decide whether to cancel our own playback.
    The processed microphone always flows, even before filter convergence.
    """
    def __init__(self):
        self.near = deque(maxlen=4)
        self.far = deque(maxlen=54)
        self.frames = 0
        self.similarity = 1.0
        self.delay_ms = 0

    def update(self, near, far):
        self.near.append(np.asarray(near).copy())
        self.far.append(np.asarray(far).copy())
        self.frames += 1
        if self.frames % 4 or len(self.near) < 4:
            return self.similarity
        near, far = np.concatenate(self.near), np.concatenate(self.far)
        near = near - near.mean()
        far = far - far.mean()
        energy = float(np.dot(near, near))
        if energy < 1e-8 or float(np.dot(far, far)) < 1e-8:
            self.similarity = 0.0
            return self.similarity
        size = 1 << (len(far) + len(near) - 2).bit_length()
        corr = np.fft.irfft(np.fft.rfft(far, size) * np.fft.rfft(near[::-1], size), size)
        corr = corr[len(near)-1:len(far)]
        sums = np.r_[0., np.cumsum(far * far)]
        window_energy = sums[len(near):] - sums[:-len(near)]
        norm = np.sqrt(np.maximum(window_energy * energy, 1e-12))
        peak=int(np.argmax(np.abs(corr) / norm))
        self.delay_ms=(len(far)-len(near)-peak)*1000/RATE
        self.similarity = float(np.clip(np.abs(corr[peak])/norm[peak], 0, 1))
        return self.similarity


def device_inventory():
    devices, hosts = sd.query_devices(), sd.query_hostapis()
    return [{'index': i, 'name': d['name'], 'inputs': d['max_input_channels'],
             'outputs': d['max_output_channels']} for i, d in enumerate(devices)
            if hosts[d['hostapi']]['name'] == 'Windows WASAPI']


def select_devices(args):
    devices = device_inventory()
    def find(name, field, virtual=False):
        candidates = [d for d in devices if d[field] > 0 and
                      ((name.casefold() in d['name'].casefold()) if name else True)]
        if not virtual:
            candidates = [d for d in candidates if not any(s in d['name'].casefold()
                          for s in ('cable', 'virtual', 'mirroring'))]
        if len(candidates) != 1:
            raise RuntimeError(f'Choose one physical {field} device; candidates: {[d["name"] for d in candidates]}')
        return candidates[0]
    mic_name = args.mic or sd.query_devices(sd.default.device[0])['name']
    speaker_name = args.speaker or sd.query_devices(sd.default.device[1])['name']
    mic, speaker = find(mic_name, 'inputs'), find(speaker_name, 'outputs')
    cable = find('CABLE Input (VB-Audio Virtual Cable)', 'outputs', virtual=True)
    return mic, speaker, cable


def run(args):
    playback = PlaybackQueue()
    dsp = DuplexProcessor(playback)
    stop = threading.Event()
    mic, speaker, cable = select_devices(args)
    dsp.status.update(physicalMic=mic['name'], speaker=speaker['name'], virtualInput='CABLE Output (VB-Audio Virtual Cable)')
    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *unused):
            pass
        def reply(self, code, payload=None):
            body = json.dumps(payload or {}).encode()
            self.send_response(code)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        def authorized(self):
            return not self.headers.get('Origin') and secrets.compare_digest(self.headers.get('X-VoiceBridge-Aec-Token', ''), args.secret)
        def do_GET(self):
            if not self.authorized():
                return self.reply(401)
            if self.path != '/status':
                return self.reply(404)
            self.reply(200, {**dsp.status, **playback.snapshot()})
        def do_POST(self):
            if not self.authorized():
                return self.reply(401)
            try:
                epoch = int(self.headers.get('X-Epoch', '-1'))
                length = int(self.headers.get('Content-Length', '0'))
                if not 0 <= length <= 32768 or epoch < 0:
                    return self.reply(400)
                pcm = self.rfile.read(length)
                if self.path == '/cancel':
                    return self.reply(200 if playback.cancel(epoch) else 410)
                if self.path == '/audio':
                    return self.reply(playback.push(epoch, pcm))
                return self.reply(404)
            except (ValueError, OSError):
                self.reply(400)
    server = ThreadingHTTPServer(('127.0.0.1', args.port), Handler)
    server.daemon_threads = True
    threading.Thread(target=server.serve_forever, daemon=True).start()
    try:
        with sd.OutputStream(device=cable['index'], channels=2, samplerate=RATE,
                             blocksize=FRAME, dtype='float32', latency=.04, callback=dsp.cable_callback), \
             sd.Stream(device=(mic['index'], speaker['index']), channels=(1, 2),
                       samplerate=RATE, blocksize=FRAME, dtype='float32', latency=.04, callback=dsp.callback) as stream:
            dsp.status.update(state='running', inputLatency=stream.latency[0], outputLatency=stream.latency[1])
            dsp.device_delay_ms = sum(stream.latency) * 1000
            started = time.monotonic()
            previous = -1
            while not stop.wait(.5):
                if args.seconds and time.monotonic() - started > args.seconds:
                    break
                if dsp.status['callbacks'] == previous:
                    raise RuntimeError('Duplex callbacks stopped')
                previous = dsp.status['callbacks']
                if args.parent_pid:
                    kernel = ctypes.windll.kernel32
                    kernel.OpenProcess.restype = ctypes.c_void_p
                    handle = kernel.OpenProcess(0x100000, False, args.parent_pid)
                    if not handle:
                        break
                    try:
                        if kernel.WaitForSingleObject(ctypes.c_void_p(handle), 0) == 0:
                            break
                    finally:
                        kernel.CloseHandle(ctypes.c_void_p(handle))
    finally:
        dsp.status['state'] = 'stopped'
        server.shutdown()
        server.server_close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--devices', action='store_true')
    parser.add_argument('--mic', default='')
    parser.add_argument('--speaker', default='')
    parser.add_argument('--port', type=int, default=17896)
    parser.add_argument('--secret', default='')
    parser.add_argument('--parent-pid', type=int, default=0)
    parser.add_argument('--seconds', type=int, default=0)
    args = parser.parse_args()
    if args.devices:
        print(json.dumps(device_inventory(), ensure_ascii=True))
    else:
        if len(args.secret) < 32:
            parser.error('IPC secret is required')
        run(args)
