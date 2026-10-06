namespace ThreadLogViewer.Core;

/// <summary>Conservative correspondence between two immutable snapshots. Ambiguous lines are never guessed.</summary>
public sealed class SourceLineRemap
{
    private readonly int[] indexes;
    public LogData Original { get; }
    public LogData Updated { get; }

    private SourceLineRemap(LogData original, LogData updated, int[] indexes)
    { Original = original; Updated = updated; this.indexes = indexes; }

    public int? Map(int oldSourceLineIndex) => oldSourceLineIndex >= 0 && oldSourceLineIndex < indexes.Length && indexes[oldSourceLineIndex] >= 0
        ? indexes[oldSourceLineIndex] : null;

    public int? FindNearestMappedLine(int oldSourceLineIndex)
    {
        if (indexes.Length == 0) return null;
        int origin = Math.Clamp(oldSourceLineIndex, 0, indexes.Length - 1);
        for (int distance = 0; distance < indexes.Length; distance++)
        {
            // A tie goes forward in the previous source order, matching the viewer's navigation policy.
            if (origin + distance < indexes.Length && indexes[origin + distance] >= 0) return indexes[origin + distance];
            if (origin - distance >= 0 && indexes[origin - distance] >= 0) return indexes[origin - distance];
        }
        return null;
    }

    public static SourceLineRemap Create(LogData original, LogData updated, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var indexes = new int[original.Lines.Count];
        Array.Fill(indexes, -1);
        bool prefix = original.Text.Length <= updated.Text.Length && original.Lines.Count <= updated.Lines.Count;
        for (int offset = 0; prefix && offset < original.Text.Length; offset += 65536)
        {
            token.ThrowIfCancellationRequested();
            int length = Math.Min(65536, original.Text.Length - offset);
            prefix = original.Text.AsSpan(offset, length).SequenceEqual(updated.Text.AsSpan(offset, length));
        }
        if (prefix)
        {
            for (int index = 0; index < indexes.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (!SameLine(original, index, updated, index)) { prefix = false; break; }
            }
            if (prefix)
            {
                for (int index = 0; index < indexes.Length; index++) indexes[index] = index;
                return new(original, updated, indexes);
            }
        }

        var oldKeys = UniqueIndexes(original, token);
        var newKeys = UniqueIndexes(updated, token);
        foreach (var pair in oldKeys)
        {
            token.ThrowIfCancellationRequested();
            if (pair.Value >= 0 && newKeys.TryGetValue(pair.Key, out int target) && target >= 0 &&
                SameLine(original, pair.Value, updated, target)) indexes[pair.Value] = target;
        }
        return new(original, updated, indexes);
    }

    public int? MapOffset(int oldOffset, bool endExclusive = false)
    {
        if (oldOffset < 0 || oldOffset > Original.Text.Length || Original.Lines.Count == 0) return null;
        int line = Original.GetLineIndexAtOffset(endExclusive && oldOffset > 0 ? oldOffset - 1 : oldOffset);
        if (Map(line) is not { } target) return null;
        int column = oldOffset - Original.GetLineOffset(line);
        var updatedLine = Updated.Lines[target];
        if (column > updatedLine.RawText.Length + updatedLine.LineEnding.Length) return null;
        return Updated.GetLineOffset(target) + column;
    }

    public SourceTextRange? MapRange(SourceTextRange range)
    {
        if (range.Offset < 0 || range.Length < 0 || range.Offset + (long)range.Length > Original.Text.Length) return null;
        if (MapOffset(range.Offset) is not { } first || MapOffset(range.Offset + range.Length, range.Length > 0) is not { } last || last < first) return null;
        int firstLine = Original.GetLineIndexAtOffset(range.Offset);
        int lastLine = Original.GetLineIndexAtOffset(range.Length == 0 ? range.Offset : range.Offset + range.Length - 1);
        if (Map(firstLine) is not { } firstTarget) return null;
        for (int line = firstLine; line <= lastLine; line++)
            if (Map(line) != firstTarget + line - firstLine) return null;
        if (!Original.Text.AsSpan(range.Offset, range.Length).SequenceEqual(Updated.Text.AsSpan(first, last - first))) return null;
        return new(first, last - first);
    }

    private readonly record struct LineKey(ulong Text, ulong Owner, int? Thread);
    private static Dictionary<LineKey, int> UniqueIndexes(LogData source, CancellationToken token)
    {
        var result = new Dictionary<LineKey, int>();
        for (int index = 0; index < source.Lines.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var line = source.Lines[index];
            var header = source.Lines[source.Entries[line.EntryIndex].StartLineIndex];
            var key = new LineKey(Hash(line.RawText.Span, token), Hash(header.RawText.Span, token), line.ThreadId);
            if (!result.TryAdd(key, index)) result[key] = -1;
        }
        return result;
    }

    private static ulong Hash(ReadOnlySpan<char> text, CancellationToken token)
    {
        ulong hash = 14695981039346656037UL;
        for (int index = 0; index < text.Length; index++)
        {
            if ((index & 65535) == 0) token.ThrowIfCancellationRequested();
            hash = unchecked((hash ^ text[index]) * 1099511628211UL);
        }
        return hash;
    }

    private static bool SameLine(LogData original, int oldIndex, LogData updated, int newIndex)
    {
        var old = original.Lines[oldIndex]; var next = updated.Lines[newIndex];
        var oldOwner = original.Lines[original.Entries[old.EntryIndex].StartLineIndex];
        var newOwner = updated.Lines[updated.Entries[next.EntryIndex].StartLineIndex];
        return old.ThreadId == next.ThreadId && old.RawText.Span.SequenceEqual(next.RawText.Span) &&
            oldOwner.RawText.Span.SequenceEqual(newOwner.RawText.Span);
    }
}
