namespace ThreadLogViewer.Core;

// Offset views into immutable source text; no parsing, header insertion or source-file writes.
public static class LogSlice
{
    public static IReadOnlyList<ThreadSummary> GetThreadSummaries(LogData source, LogLineRange? lineRange = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        if (lineRange is not { } scope) return source.Threads;
        scope.Validate(source);
        var summaries = new Dictionary<int, (int Count, int Entries, string? First, string? Last)>();
        int first = source.Lines[scope.FirstLineIndex].EntryIndex;
        int last = source.Lines[scope.LastLineIndex].EntryIndex;
        for (int i = first; i <= last; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = source.Entries[i];
            int key = entry.ThreadId ?? -1;
            var summary = summaries.GetValueOrDefault(key);
            summary.Count += Math.Min(entry.StartLineIndex + entry.LineCount - 1, scope.LastLineIndex) -
                Math.Max(entry.StartLineIndex, scope.FirstLineIndex) + 1;
            summary.Entries++;
            var header = source.Lines[entry.StartLineIndex];
            if (header.TimeOfDay is not null)
            {
                summary.First ??= header.TimestampText.ToString();
                summary.Last = header.TimestampText.ToString();
            }
            summaries[key] = summary;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Array.AsReadOnly(summaries.OrderBy(p => p.Key == -1 ? long.MaxValue : p.Key)
            .Select(p => new ThreadSummary(p.Key == -1 ? null : p.Key, p.Value.Count, p.Value.First, p.Value.Last, p.Value.Entries)).ToArray());
    }

    public static SourceTextRange GetTextRange(LogData source, LogLineRange? lineRange = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (lineRange is not { } scope) return new(0, source.Text.Length);
        scope.Validate(source);
        int first = source.GetLineOffset(scope.FirstLineIndex);
        return new(first, source.GetLineOffset(scope.LastLineIndex + 1) - first);
    }

    public static SourceTextRange? GetEntryRange(LogData source, int entryIndex, LogLineRange? lineRange = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (lineRange is null) return source.GetEntryRange(entryIndex);
        lineRange.Value.Validate(source);
        if (entryIndex < 0 || entryIndex >= source.Entries.Count) throw new ArgumentOutOfRangeException(nameof(entryIndex));
        var entry = source.Entries[entryIndex];
        int first = Math.Max(entry.StartLineIndex, lineRange.Value.FirstLineIndex);
        int last = Math.Min(entry.StartLineIndex + entry.LineCount - 1, lineRange.Value.LastLineIndex);
        if (first > last) return null;
        int offset = source.GetLineOffset(first);
        return new(offset, source.GetLineOffset(last + 1) - offset);
    }
}
