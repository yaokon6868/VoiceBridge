import importlib.util
from pathlib import Path
import unittest
import numpy as np
from types import SimpleNamespace

spec = importlib.util.spec_from_file_location('engine', Path(__file__).parents[2] / 'aec' / 'engine.py')
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)


class Tests(unittest.TestCase):
    def test_cold_echo_does_not_barge_but_near_speech_does(self):
        rng=np.random.default_rng(83)
        q=engine.PlaybackQueue(capacity=44100*2*5)
        pcm=(rng.normal(0,.07,44100*4)*32767).astype('<i2')
        for start in range(0,len(pcm),16384):
            self.assertEqual(q.push(0,pcm[start:start+16384].tobytes()),202)
        dsp=engine.DuplexProcessor(q)
        # A confident speech detector also sees the spoken speaker echo.
        # The interruption decision must independently reject that echo.
        dsp.vad=SimpleNamespace(process=lambda _:1.)
        far_history=[]
        timing=SimpleNamespace(inputBufferAdcTime=1.,outputBufferDacTime=1.)
        flags=SimpleNamespace(input_overflow=False,output_underflow=False)
        out=np.zeros((480,2),np.float32)
        for frame in range(250):
            near=far_history[-5]*.45 if len(far_history)>=5 else np.zeros(480,np.float32)
            dsp.callback(near[:,None],out,480,timing,None)
            far_history.append(out[:,0].copy())
        self.assertEqual(dsp.status['bargeIns'],0)
        self.assertAlmostEqual(dsp.estimated_delay_ms,50.,delta=10.)
        for frame in range(30):
            near=far_history[-5]*.45+rng.normal(0,.06,480).astype(np.float32)
            dsp.callback(near[:,None],out,480,timing,None)
            far_history.append(out[:,0].copy())
            if dsp.status['bargeIns']:break
        self.assertEqual(dsp.status['bargeIns'],1)
        self.assertLess(frame,20)
        self.assertGreater(dsp.status['cleanRms'],.01)
        self.assertEqual(q.snapshot()['bufferedBytes'],0)

    def test_epochs_and_backpressure(self):
        q = engine.PlaybackQueue(capacity=1000)
        self.assertEqual(q.push(10, bytes(882)), 202)
        self.assertEqual(q.push(10, bytes(882)), 429)
        self.assertEqual(q.push(9, bytes(10)), 410)
        q.cancel(11)
        self.assertEqual(q.snapshot()['bufferedBytes'], 0)
        self.assertEqual(q.push(10, bytes(10)), 410)
        self.assertEqual(q.push(11, bytes(882)), 202)
        self.assertEqual(q.read().shape, (480,))

    def test_not_a_mic_gate(self):
        dsp = engine.DuplexProcessor(engine.PlaybackQueue())
        rng = np.random.default_rng(12)
        near = rng.normal(0, .03, engine.RATE * 2).astype(np.float32)
        clean = np.concatenate([dsp.process(near[i:i+480], np.zeros(480, np.float32), 50)
                                for i in range(0, len(near), 480)])
        self.assertGreater(np.sqrt(np.mean(clean ** 2) / np.mean(near ** 2)), .7)

    def test_echo_and_double_talk(self):
        rng = np.random.default_rng(77)
        frames = engine.RATE * 20
        far = rng.normal(0, .08, frames).astype(np.float32)
        far = np.convolve(far, np.ones(5)/5, mode='same').astype(np.float32)
        echo = np.zeros_like(far)
        echo[2400:] = far[:-2400] * .45
        dsp = engine.DuplexProcessor(engine.PlaybackQueue())
        cleaned = np.concatenate([dsp.process(echo[i:i+480], far[i:i+480], 50)
                                  for i in range(0, frames, 480)])
        tail = slice(engine.RATE * 15, None)
        reduction = 10 * np.log10((np.mean(echo[tail]**2)+1e-12)/(np.mean(cleaned[tail]**2)+1e-12))
        self.assertGreater(reduction, 12)
        # Independent near-end signal while render continues. No output gating.
        near = rng.normal(0, .04, engine.RATE * 3).astype(np.float32)
        far2 = rng.normal(0, .06, len(near)).astype(np.float32)
        echo2 = np.r_[far[-2400:], far2[:-2400]] * .45
        mixed = near + echo2
        result = np.concatenate([dsp.process(mixed[i:i+480], far2[i:i+480], 50)
                                 for i in range(0, len(near), 480)])
        retained = float(np.dot(result, near) / np.dot(near, near))
        self.assertGreater(retained, .6)
        print({'syntheticEchoReductionDb': round(float(reduction), 2), 'doubleTalkNearRetention': round(retained, 3)})


if __name__ == '__main__':
    unittest.main()
