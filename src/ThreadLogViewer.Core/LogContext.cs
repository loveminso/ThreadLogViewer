namespace ThreadLogViewer.Core;

public static class LogContext
{
    public static LogProjection Create(LogData source, int sourceLineIndex, int radius = 20,
        CancellationToken cancellationToken = default, LogLineRange? lineRange = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lineRange?.Validate(source);
        if (sourceLineIndex < 0 || sourceLineIndex >= source.Lines.Count) throw new ArgumentOutOfRangeException(nameof(sourceLineIndex));
        if (lineRange is { } scope && !scope.Contains(sourceLineIndex)) throw new ArgumentOutOfRangeException(nameof(sourceLineIndex));
        if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
        int center = source.Lines[sourceLineIndex].EntryIndex;
        int start = (int)Math.Max(0, center - (long)radius);
        int end = (int)Math.Min(source.Entries.Count - 1L, center + (long)radius);
        if (lineRange is { } bounds)
        {
            start = Math.Max(start, source.Lines[bounds.FirstLineIndex].EntryIndex);
            end = Math.Min(end, source.Lines[bounds.LastLineIndex].EntryIndex);
        }
        return LogProjection.CreateEntries(source, Enumerable.Range(start, end - start + 1), cancellationToken, lineRange: lineRange);
    }
}
