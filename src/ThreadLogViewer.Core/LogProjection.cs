using System.Text;

namespace ThreadLogViewer.Core;

public sealed class LogProjection
{
    private readonly int[] sourceIndexes;
    private readonly int[] displayOffsets;

    // Retain the old constructor for callers that already have a projection.
    public LogProjection(LogData source, int[] sourceIndexes, string text, HashSet<int?> selectedThreads)
        : this(source, sourceIndexes, text, selectedThreads,
            sourceIndexes.Select(i => source.Lines[i].EntryIndex).Distinct().ToArray()) { }

    private LogProjection(LogData source, int[] sourceIndexes, string text, HashSet<int?> selectedThreads, int[] entryIndexes)
    {
        Source = source;
        this.sourceIndexes = sourceIndexes;
        SourceIndexes = Array.AsReadOnly(sourceIndexes);
        EntryIndexes = Array.AsReadOnly(entryIndexes);
        Text = text;
        SelectedThreads = new HashSet<int?>(selectedThreads);
        displayOffsets = new int[sourceIndexes.Length + 1];
        for (int i = 0; i < sourceIndexes.Length; i++)
        {
            var line = source.Lines[sourceIndexes[i]];
            displayOffsets[i + 1] = checked(displayOffsets[i] + line.RawText.Length + line.LineEnding.Length);
        }
        DisplayOffsets = Array.AsReadOnly(displayOffsets);
    }

    public LogData Source { get; }
    public IReadOnlyList<int> SourceIndexes { get; }
    public IReadOnlyList<int> EntryIndexes { get; }
    public IReadOnlyList<int> DisplayOffsets { get; }
    public string Text { get; }
    public IReadOnlySet<int?> SelectedThreads { get; }
    public int Count => SourceIndexes.Count;
    public int EntryCount => EntryIndexes.Count;
    public LogLine? AtDisplayLine(int oneBasedLine) => oneBasedLine >= 1 && oneBasedLine <= Count
        ? Source.Lines[SourceIndexes[oneBasedLine - 1]] : null;

    public int? FindDisplayLine(int originalOneBasedLine, bool nearest = false)
    {
        if (originalOneBasedLine < 1 || originalOneBasedLine > Source.Lines.Count || Count == 0) return null;
        int sourceIndex = originalOneBasedLine - 1;
        int found = Array.BinarySearch(sourceIndexes, sourceIndex);
        if (found >= 0) return found + 1;
        if (!nearest) return null;
        int next = ~found;
        if (next == 0) return 1;
        if (next == Count) return Count;
        // A tie goes forward in source order.
        return sourceIndex - sourceIndexes[next - 1] < sourceIndexes[next] - sourceIndex ? next : next + 1;
    }

    public int? GetDisplayOffset(int sourceOffset)
    {
        if (sourceOffset < 0 || sourceOffset > Source.Text.Length || Count == 0) return null;
        int sourceLine = Source.GetLineIndexAtOffset(sourceOffset);
        int index = Array.BinarySearch(sourceIndexes, sourceLine);
        return index < 0 ? null : displayOffsets[index] + sourceOffset - Source.GetLineOffset(sourceLine);
    }

    public int? GetSourceOffset(int displayOffset)
    {
        if (displayOffset < 0 || displayOffset > Text.Length || Count == 0) return null;
        int found = Array.BinarySearch(displayOffsets, 0, Count, displayOffset);
        int index = found >= 0 ? found : Math.Max(0, ~found - 1);
        return Source.GetLineOffset(sourceIndexes[index]) + displayOffset - displayOffsets[index];
    }

    public static LogProjection Create(LogData source, IEnumerable<int?> selectedThreads,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null) =>
        CreateFiltered(source, selectedThreads, EntryFilter.Empty, cancellationToken, progress);

    public static LogProjection CreateFiltered(LogData source, IEnumerable<int?> selectedThreads, EntryFilter filter,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null, LogLineRange? lineRange = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lineRange?.Validate(source);
        var selected = selectedThreads.ToHashSet();
        var conditions = filter.Prepare();
        var entries = new List<int>();
        int firstEntry = lineRange is { } scope ? source.Lines[scope.FirstLineIndex].EntryIndex : 0;
        int lastEntry = lineRange is { } lastScope ? source.Lines[lastScope.LastLineIndex].EntryIndex : source.Entries.Count - 1;
        for (int i = firstEntry; i <= lastEntry; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (((i - firstEntry) & 2047) == 0) progress?.Report(new("필터 적용", 50.0 * (i - firstEntry) / Math.Max(1, lastEntry - firstEntry + 1)));
            if (!selected.Contains(source.Entries[i].ThreadId)) continue;
            var range = LogSlice.GetEntryRange(source, i, lineRange)!.Value;
            if (conditions.Matches(source.Text.AsSpan(range.Offset, range.Length), cancellationToken)) entries.Add(i);
        }
        return Build(source, entries.ToArray(), selected, cancellationToken, progress, lineRange);
    }

    public static LogProjection CreateLineRange(LogData source, LogLineRange lineRange,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lineRange.Validate(source);
        int first = source.Lines[lineRange.FirstLineIndex].EntryIndex;
        int last = source.Lines[lineRange.LastLineIndex].EntryIndex;
        return CreateEntries(source, Enumerable.Range(first, last - first + 1), cancellationToken, lineRange: lineRange);
    }

    public static LogProjection CreateEntries(LogData source, IEnumerable<int> entryIndexes,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null, LogLineRange? lineRange = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lineRange?.Validate(source);
        var indexes = new SortedSet<int>();
        foreach (int index in entryIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index < 0 || index >= source.Entries.Count) throw new ArgumentOutOfRangeException(nameof(entryIndexes));
            if (LogSlice.GetEntryRange(source, index, lineRange) is not null) indexes.Add(index);
        }
        int[] ordered = indexes.ToArray();
        return Build(source, ordered, ordered.Select(i => source.Entries[i].ThreadId).ToHashSet(), cancellationToken, progress, lineRange);
    }

    private static LogProjection Build(LogData source, int[] entries, HashSet<int?> selected,
        CancellationToken token, IProgress<WorkProgress>? progress, LogLineRange? lineRange = null)
    {
        bool all = entries.Length == source.Entries.Count &&
            (lineRange is null || lineRange.Value.FirstLineIndex == 0 && lineRange.Value.LastLineIndex == source.Lines.Count - 1);
        var builder = all ? null : new StringBuilder();
        var lines = new List<int>();
        for (int index = 0; index < entries.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var entry = source.Entries[entries[index]];
            int firstLine = Math.Max(entry.StartLineIndex, lineRange?.FirstLineIndex ?? 0);
            int lastLine = Math.Min(entry.StartLineIndex + entry.LineCount - 1, lineRange?.LastLineIndex ?? source.Lines.Count - 1);
            for (int i = firstLine; i <= lastLine; i++)
            {
                if ((i & 4095) == 0)
                {
                    token.ThrowIfCancellationRequested();
                    progress?.Report(new("필터 적용", 50 + 50.0 * index / Math.Max(1, entries.Length)));
                }
                lines.Add(i);
                var line = source.Lines[i];
                builder?.Append(line.RawText.Span).Append(line.LineEnding.Span);
            }
        }
        token.ThrowIfCancellationRequested();
        var result = new LogProjection(source, lines.ToArray(), all ? source.Text : builder!.ToString(), selected, entries);
        token.ThrowIfCancellationRequested();
        progress?.Report(new("필터 적용", 100));
        return result;
    }
}
