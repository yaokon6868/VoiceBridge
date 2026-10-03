using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using System.Buffers.Binary;

namespace VoiceBridge;

// Experimental: only Fish's actual output frames are sent to a local AEC helper.
// This never reads a system loopback device or microphone.
public sealed class EchoReferenceProvider : IWaveProvider, IDisposable
{
    private readonly IWaveProvider _source;
    private readonly Socket _sender = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private readonly EndPoint _destination = new IPEndPoint(IPAddress.Loopback, 17894);
    private readonly byte[] _frame = new byte[960];
    private readonly byte[] _packet = new byte[976];
    private readonly object _sync = new();
    private long _epoch = DateTime.UtcNow.Ticks;
    private int _sequence;
    private int _used;
    public WaveFormat WaveFormat => _source.WaveFormat;
    public EchoReferenceProvider(IWaveProvider source)
    {
        if(source.WaveFormat.SampleRate != 48000 || source.WaveFormat.BitsPerSample != 16 || source.WaveFormat.Channels != 1)
            throw new ArgumentException("Echo reference requires 48kHz mono PCM16");
        _source = source;
        _sender.Blocking = false;
    }
    public int Read(byte[] buffer, int offset, int count)
    {
        lock (_sync)
        {
        var read = _source.Read(buffer, offset, count);
        for (var consumed = 0; consumed < read;)
        {
            var copy = Math.Min(_frame.Length - _used, read - consumed);
            Buffer.BlockCopy(buffer, offset + consumed, _frame, _used, copy);
            _used += copy; consumed += copy;
            if (_used != _frame.Length) continue;
            "VBR1"u8.CopyTo(_packet);
            BinaryPrimitives.WriteInt64LittleEndian(_packet.AsSpan(4), _epoch);
            BinaryPrimitives.WriteInt32LittleEndian(_packet.AsSpan(12), _sequence++);
            _frame.CopyTo(_packet, 16);
            try { _sender.SendTo(_packet, _destination); } catch(SocketException) { } catch(ObjectDisposedException) { }
            _used = 0;
        }
        return read;
        }
    }
    public void Reset()
    {
        lock (_sync)
        {
            _used = 0; _sequence = 0; _epoch++;
            "VBR0"u8.CopyTo(_packet);
            BinaryPrimitives.WriteInt64LittleEndian(_packet.AsSpan(4), _epoch);
            BinaryPrimitives.WriteInt32LittleEndian(_packet.AsSpan(12), 0);
            try { _sender.SendTo(_packet, 0, 16, SocketFlags.None, _destination); }
            catch(SocketException) { } catch(ObjectDisposedException) { }
        }
    }
    public void Dispose() => _sender.Dispose();
}
