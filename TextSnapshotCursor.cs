namespace VoiceBridge;

// A submitted cursor belongs to its snapshot. Revisions must move that cursor
// with the text, rather than treating it as a fixed index in the next snapshot.
internal static class TextSnapshotCursor
{
    public static int Rebase(string before, string after, int cursor, out bool limited)
    {
        limited = false;
        cursor = Math.Clamp(cursor, 0, before.Length);
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        // Left bias: an insertion exactly at the cursor is still unread text.
        if (cursor <= prefix) return cursor;
        // Providers often rewrite an already spoken comma or paragraph break.
        // Consume its new separator form without treating it as a new phrase.
        if (prefix < after.Length && OnlySeparators(before.AsSpan(prefix, cursor - prefix)))
        {
            var end = prefix;
            var mapped = true;
            for (var i = prefix; i < cursor; i++)
            {
                if (end >= after.Length || !EquivalentSeparator(before[i], after[end])) { mapped = false; break; }
                if (before[i] == '\n' && after[end] == '\r' && end + 1 < after.Length && after[end + 1] == '\n') end++;
                end++;
            }
            if (mapped) return end;
        }
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;
        var oldEnd = before.Length - suffix;
        var newEnd = after.Length - suffix;
        if (cursor > oldEnd) return Math.Clamp(cursor + newEnd - oldEnd, 0, after.Length);

        // A unique unchanged committed ending keeps edits and large appends out
        // of the alignment calculation. Never choose between repeated anchors.
        for (var length = Math.Min(cursor, 64); length >= 8; length--)
        {
            var anchor = before.Substring(cursor - length, length);
            var at = after.IndexOf(anchor, StringComparison.Ordinal);
            if (at >= prefix && at == after.LastIndexOf(anchor, StringComparison.Ordinal)) return at + length;
        }

        // Myers alignment of only the changed range. Bound work/memory for
        // provider rewrites; ordinary appended snapshots take the fast path.
        var oldText = before.AsSpan(prefix, oldEnd - prefix);
        var newText = after.AsSpan(prefix, newEnd - prefix);
        const int budget = 128;
        const int zero = budget + 1;
        var furthest = new int[2 * budget + 3];
        var trace = new List<int[]>();
        for (var edits = 0; edits <= budget; edits++)
        {
            for (var diagonal = -edits; diagonal <= edits; diagonal += 2)
            {
                var x = diagonal == -edits || (diagonal != edits && furthest[zero + diagonal - 1] < furthest[zero + diagonal + 1])
                    ? furthest[zero + diagonal + 1] : furthest[zero + diagonal - 1] + 1;
                var y = x - diagonal;
                while (x < oldText.Length && y < newText.Length && oldText[x] == newText[y]) { x++; y++; }
                furthest[zero + diagonal] = x;
                if (x < oldText.Length || y < newText.Length) continue;
                var operations = new List<char>();
                for (var step = edits; step > 0; step--)
                {
                    var previous = trace[step - 1];
                    var k = x - y;
                    var previousK = k == -step || (k != step && previous[zero + k - 1] < previous[zero + k + 1]) ? k + 1 : k - 1;
                    var previousX = previous[zero + previousK];
                    var previousY = previousX - previousK;
                    while (x > previousX && y > previousY) { operations.Add('='); x--; y--; }
                    if (x == previousX) { operations.Add('+'); y--; }
                    else { operations.Add('-'); x--; }
                }
                while (x > 0 && y > 0) { operations.Add('='); x--; y--; }
                var oldPosition = prefix;
                var newPosition = prefix;
                for (var i = operations.Count - 1; i >= 0; i--)
                {
                    if (oldPosition == cursor) return newPosition;
                    if (operations[i] != '+') oldPosition++;
                    if (operations[i] != '-') newPosition++;
                }
                return newPosition;
            }
            trace.Add((int[])furthest.Clone());
        }
        // Keep the existing conservative position for a wholesale unalignable
        // rewrite, and expose it in diagnostics instead of claiming alignment.
        limited = true;
        return Math.Min(cursor, after.Length);
    }
    private static bool IsSeparator(char value) => char.IsPunctuation(value) || char.IsWhiteSpace(value);
    private static bool EquivalentSeparator(char before, char after)
    {
        static char Fold(char value) => value switch { '，' => ',', '；' => ';', '。' => '.', '？' => '?', '！' => '!', '：' => ':', '\r' => '\n', _ => value };
        return Fold(before) == Fold(after) || (char.IsWhiteSpace(before) && char.IsWhiteSpace(after));
    }
    private static bool OnlySeparators(ReadOnlySpan<char> value)
    {
        foreach (var character in value) if (!IsSeparator(character)) return false;
        return value.Length > 0;
    }
}
