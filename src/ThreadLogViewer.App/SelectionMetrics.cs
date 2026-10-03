namespace ThreadLogViewer.App;

public readonly record struct SelectionSpan(int Offset, int Length);
public readonly record struct SelectionSummary(int LineCount, long ScalarCount);

public static class SelectionMetrics
{
    /// <summary>Convenience overload; repeated calculations can share precomputed line offsets.</summary>
    public static SelectionSummary Compute(string text, int physicalLineCount,
        IEnumerable<SelectionSpan> segments, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(physicalLineCount);
        cancellationToken.ThrowIfCancellationRequested();
        SelectionSpan[] snapshot = segments.ToArray();
        if (snapshot.Length == 0) return new(0, 0);
        var offsets = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                offsets.Add(i + 1);
            }
            else if (text[i] == '\n') offsets.Add(i + 1);
        }
        return Compute(text, offsets, physicalLineCount, snapshot, cancellationToken);
    }

    /// <summary>Counts the union of [start,end) selections without allocating SelectedText.
    /// Newlines count as scalars (CRLF is two); a trailing editor-only row is excluded.</summary>
    public static SelectionSummary Compute(string text, IReadOnlyList<int> lineOffsets, int physicalLineCount,
        IEnumerable<SelectionSpan> segments, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(physicalLineCount);
        if (physicalLineCount > lineOffsets.Count) throw new ArgumentException("줄 오프셋이 부족합니다.", nameof(lineOffsets));
        var ranges = segments.Select(s =>
        {
            if (s.Offset < 0 || s.Length < 0 || (long)s.Offset + s.Length > text.Length)
                throw new ArgumentOutOfRangeException(nameof(segments));
            return (Start: s.Offset, End: s.Offset + s.Length);
        }).Where(s => s.Start < s.End).OrderBy(s => s.Start).ToArray();

        long scalars = 0;
        int lines = 0, lastCountedLine = -1;
        for (int r = 0; r < ranges.Length; r++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int start = ranges[r].Start, end = ranges[r].End;
            while (r + 1 < ranges.Length && ranges[r + 1].Start <= end) end = Math.Max(end, ranges[++r].End);
            int first = FindLine(lineOffsets, physicalLineCount, start);
            int last = FindLine(lineOffsets, physicalLineCount, end - 1);
            if (first >= 0 && last >= 0)
            {
                int firstNew = Math.Max(first, lastCountedLine + 1);
                if (last >= firstNew) lines += last - firstNew + 1;
                lastCountedLine = Math.Max(lastCountedLine, last);
            }
            for (int i = start; i < end; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                scalars++;
                if (char.IsHighSurrogate(text[i]) && i + 1 < end && char.IsLowSurrogate(text[i + 1])) i++;
            }
        }
        return new(lines, scalars);
    }

    private static int FindLine(IReadOnlyList<int> offsets, int count, int offset)
    {
        int low = 0, high = count;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (offsets[mid] <= offset) low = mid + 1; else high = mid;
        }
        return low - 1;
    }
}
