using NAudio.Wave;
namespace VoiceBridge;

// One owner for physical playback, independent of WebSocket lifetime.
public sealed class AudioPlayback : IAudioPlayback
{
    private readonly object _sync = new();
    private BufferedWaveProvider? _buffer;
    private WaveOutEvent? _device;
    private EchoReferenceProvider? _reference;
    private long _epoch;
    private long _received;
    public event Action<string>? Failed;
    public long BeginTurn() { lock (_sync) { StopLocked(); return _epoch; } }
    public void EnsureStarted(bool echoReference, long epoch)
    {
        lock (_sync)
        {
            if (epoch != _epoch) return;
            if (_device is null)
            {
                _buffer = new BufferedWaveProvider(new WaveFormat(44100,16,1))
                { BufferDuration = TimeSpan.FromSeconds(45), DiscardOnBufferOverflow = false };
                _device = new WaveOutEvent { DesiredLatency = 180, NumberOfBuffers = 3 };
                var createdDevice = _device;
                _device.PlaybackStopped += (_, e) => { if (e.Exception is not null && ReferenceEquals(_device,createdDevice)) Failed?.Invoke("audio-device:"+e.Exception.GetType().Name); };
                IWaveProvider source = _buffer;
                if (echoReference)
                {
                    var samples = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(_buffer.ToSampleProvider(),48000);
                    _reference = new EchoReferenceProvider(new NAudio.Wave.SampleProviders.SampleToWaveProvider16(samples));
                    source = _reference;
                }
                _device.Init(source);
            }
            _device.Play();
        }
    }
    public bool Enqueue(byte[] pcm, long epoch)
    {
        lock (_sync)
        {
            if (epoch != _epoch || _buffer is null) return false;
            _buffer.AddSamples(pcm,0,pcm.Length); _received += pcm.Length; return true;
        }
    }
    public object Snapshot() { lock (_sync) return new { state = _device?.PlaybackState.ToString() ?? "idle", bufferedBytes = _buffer?.BufferedBytes ?? 0, receivedBytes = _received, epoch = _epoch }; }
    private void StopLocked()
    {
        _epoch++; _device?.Stop(); _buffer?.ClearBuffer(); _reference?.Reset();
    }
    public void Stop() { lock (_sync) StopLocked(); }
    public long ResetDevice()
    {
        lock (_sync)
        {
            var old = _device; _device = null; _buffer = null;
            _reference?.Dispose(); _reference = null; _epoch++;
            try { old?.Stop(); } catch { }
            try { old?.Dispose(); } catch { }
            return _epoch;
        }
    }
    public void Dispose() => ResetDevice();
}
