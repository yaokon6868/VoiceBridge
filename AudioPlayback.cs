using NAudio.Wave;
namespace VoiceBridge;

// One owner for physical playback, independent of WebSocket lifetime.
public sealed class AudioPlayback : IAudioPlayback
{
    private readonly object _sync = new();
    private BufferedWaveProvider? _buffer;
    private WaveOutEvent? _device;
    private long _epoch;
    private long _received;
    private readonly AecHelper? _aec;
    private bool _duplex;
    private Task _cancel=Task.CompletedTask;
    public AudioPlayback(AecHelper? aec=null) => _aec=aec;
    public event Action<string>? Failed;
    public bool CanStart(bool duplex) => !duplex || _aec?.Ready==true;
    public long BeginTurn() { lock (_sync) { StopLocked(); return _epoch; } }
    public void EnsureStarted(bool echoReference, long epoch)
    {
        lock (_sync)
        {
            if (epoch != _epoch) return;
            _duplex=echoReference;
            if(_duplex)
            {
                if(_aec?.Ready!=true)throw new IOException("Duplex engine is unavailable");
                return;
            }
            if (_device is null)
            {
                _buffer = new BufferedWaveProvider(new WaveFormat(44100,16,1))
                { BufferDuration = TimeSpan.FromSeconds(45), DiscardOnBufferOverflow = false };
                _device = new WaveOutEvent { DesiredLatency = 180, NumberOfBuffers = 3 };
                var createdDevice = _device;
                _device.PlaybackStopped += (_, e) => { if (e.Exception is not null && ReferenceEquals(_device,createdDevice)) Failed?.Invoke("audio-device:"+e.Exception.GetType().Name); };
                IWaveProvider source = _buffer;
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
            if(pcm.Length>_buffer.BufferLength-_buffer.BufferedBytes)return false;
            _buffer.AddSamples(pcm,0,pcm.Length); _received += pcm.Length; return true;
        }
    }
    public async ValueTask<bool> EnqueueAsync(byte[] pcm,long epoch,CancellationToken token)
    {
        Task cancel;bool duplex;
        lock(_sync){if(epoch!=_epoch)return false;cancel=_cancel;duplex=_duplex;}
        if(duplex)
        {
            await cancel.WaitAsync(token);
            return await _aec!.EnqueueAsync(pcm,epoch,token);
        }
        for(var offset=0;offset<pcm.Length;)
        {
            token.ThrowIfCancellationRequested();
            var length=Math.Min(16384,pcm.Length-offset);
            lock(_sync)
            {
                if(epoch!=_epoch || _buffer is null)return false;
                if(_buffer.BufferLength-_buffer.BufferedBytes>=length)
                {_buffer.AddSamples(pcm,offset,length);_received+=length;offset+=length;continue;}
            }
            await Task.Delay(15,token);
        }
        return true;
    }
    public object Snapshot() { lock (_sync) return new { state = _duplex?"duplex":_device?.PlaybackState.ToString() ?? "idle", bufferedBytes = _buffer?.BufferedBytes ?? 0, receivedBytes = _received, epoch = _epoch, duplex=_duplex }; }
    private void StopLocked()
    {
        _epoch++; _device?.Stop(); _buffer?.ClearBuffer();
        if(_aec is not null)_cancel=_aec.CancelAsync(_epoch);
    }
    public void Stop() { lock (_sync) StopLocked(); }
    public long ResetDevice()
    {
        lock (_sync)
        {
            var old = _device; _device = null; _buffer = null;
            _epoch++; _duplex=false;
            if(_aec is not null)_cancel=_aec.CancelAsync(_epoch);
            try { old?.Stop(); } catch { }
            try { old?.Dispose(); } catch { }
            return _epoch;
        }
    }
    public void Dispose() => ResetDevice();
}
