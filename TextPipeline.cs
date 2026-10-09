using System.Text;
namespace VoiceBridge;

// Captions are immediate; synthesis receives coherent turns, never token-sized chunks.
public sealed class TextPipeline : IDisposable
{
    private readonly object _sync = new();
    private string? _active;
    private string _snapshot = "";
    private int _submittedLength;
    private long _turnSerial;
    private int _emittedPhrases, _emittedChars, _revisionCount, _alignmentLimited, _completeEvents;
    private object? _previous;
    private const int UnpunctuatedMinimum = 16;
    private readonly StringBuilder _pending = new();
    private CancellationTokenSource? _delay;
    public event Action<string>? CaptionChanged;
    public event Action<string>? PhraseReady;
    public event Action? Interrupted;
    public TextPipeline(AppSettings settings) { }
    public void Handle(BridgeEvent ev)
    {
        lock (_sync)
        {
            var key = $"{ev.Source}:{ev.SessionId}";
            if (ev.Type == "start")
            {
                if (_active == key) return;
                Reset(); _active = key;
                _turnSerial++;
                CaptionChanged?.Invoke("");
                return;
            }
            // An idle tab/other desktop instance must not cancel the active voice.
            if (ev.Type is "stop" or "interrupt")
            {
                if (_active == key) Reset();
                return;
            }
            if (_active != key) return;
            if (ev.Type == "complete")
            {
                _completeEvents++;
                // Some accessibility providers expose 'complete' while text is still
                // arriving. Coalesce repeated completion notifications too.
                if (_pending.Length > 0 && !_completing) Schedule(200, true);
                return;
            }
            if (ev.Type != "text" || string.IsNullOrEmpty(ev.Text)) return;
            string delta;
            if (ev.Mode == "delta") { delta = ev.Text; _snapshot += delta; }
            else
            {
                if (!ev.Text.StartsWith(_snapshot, StringComparison.Ordinal))
                {
                    // DOM snapshots can revise punctuation or words before the tail.
                    // Rebuild only text not yet submitted, rather than dropping the update.
                    _revisionCount++;
                    _submittedLength = TextSnapshotCursor.Rebase(_snapshot, ev.Text, _submittedLength, out var limited);
                    if (limited) _alignmentLimited++;
                    _snapshot = ev.Text;
                    _pending.Clear();
                    _pending.Append(ev.Text[Math.Min(_submittedLength, ev.Text.Length)..]);
                    CaptionChanged?.Invoke(_snapshot);
                    if (_pending.Length > 0) Schedule(900, false);
                    else { _delay?.Cancel(); _completing = false; }
                    return;
                }
                delta = ev.Text[_snapshot.Length..];
                _snapshot = ev.Text;
            }
            CaptionChanged?.Invoke(_snapshot);
            if (delta.Length == 0) return;
            _pending.Append(delta);
            // Send a useful phrase at punctuation; merge very short fragments.
            var boundary = -1;
            for (var i = 0; i < _pending.Length; i++)
            {
                if (i < 7 || !"，,；;。！？!?\n".Contains(_pending[i])) continue;
                // An ASCII comma following a digit may be a thousands separator.
                if (_pending[i] == ',' && char.IsDigit(_pending[i - 1])) continue;
                boundary = i + 1;
            }
            if (boundary > 0)
            {
                _delay?.Cancel(); _completing = false;
                var sentence = _pending.ToString(0, boundary);
                _pending.Remove(0, boundary);
                _submittedLength += boundary;
                Emit(sentence);
            }
            if (_pending.Length > 0) Schedule(900, false);
        }
    }
    private bool _completing;
    private void Schedule(int milliseconds, bool completing)
    {
        _delay?.Cancel(); _delay?.Dispose();
        _delay = new CancellationTokenSource();
        var token = _delay.Token;
        _completing = completing;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(milliseconds, token);
                lock (_sync)
                {
                    if (token.IsCancellationRequested) return;
                    // A quiet DOM interval is not proof that a short phrase ended.
                    // Keep tiny fragments until punctuation or actual completion.
                    if (!completing && _pending.Length < UnpunctuatedMinimum) return;
                    var phrase = _pending.ToString(); _submittedLength += _pending.Length; _pending.Clear(); _completing = false;
                    if (!string.IsNullOrWhiteSpace(phrase)) Emit(phrase);
                }
            }
            catch (OperationCanceledException) { }
        });
    }
    private void Reset()
    {
        if (_active is not null) _previous = Describe(ended: true);
        _delay?.Cancel(); _delay?.Dispose(); _delay = null;
        _active = null; _snapshot = ""; _submittedLength = 0; _pending.Clear(); _completing = false;
        _emittedPhrases = _emittedChars = _revisionCount = _alignmentLimited = _completeEvents = 0;
        Interrupted?.Invoke();
    }
    private void Emit(string phrase)
    {
        _emittedPhrases++; _emittedChars += phrase.Length;
        PhraseReady?.Invoke(phrase);
    }
    private object Describe(bool ended = false) => new {
        turnSerial = _turnSerial, active = !ended && _active is not null,
        snapshotChars = _snapshot.Length, cursorChars = _submittedLength,
        pendingChars = _pending.Length, emittedPhrases = _emittedPhrases, emittedChars = _emittedChars,
        revisionCount = _revisionCount, alignmentLimited = _alignmentLimited,
        completeEvents = _completeEvents
    };
    public object Snapshot() { lock (_sync) return new { current = Describe(), previous = _previous }; }
    public void InterruptAll() { lock (_sync) Reset(); }
    public void Dispose() => InterruptAll();
}
