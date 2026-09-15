using System.Text;

namespace ThreadLogViewer.Core;

public sealed class LogProjection(LogData source, int[] sourceIndexes, string text, HashSet<int?> selectedThreads)
{
    public LogData Source { get; } = source;
    public IReadOnlyList<int> SourceIndexes { get; } = Array.AsReadOnly(sourceIndexes);
    public string Text { get; } = text;
    public IReadOnlySet<int?> SelectedThreads { get; } = new HashSet<int?>(selectedThreads);
    public int Count => SourceIndexes.Count;
    public LogLine? AtDisplayLine(int oneBasedLine) => oneBasedLine >= 1 && oneBasedLine <= Count
        ? Source.Lines[SourceIndexes[oneBasedLine - 1]] : null;

    public static LogProjection Create(LogData source, IEnumerable<int?> selectedThreads,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null)
    {
        var selected = selectedThreads.ToHashSet();
        var indexes = new List<int>();
        bool all = source.Threads.All(t => selected.Contains(t.ThreadId));
        var builder = all ? null : new StringBuilder();
        for (int i = 0; i < source.Lines.Count; i++)
        {
            if ((i & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new("필터 적용", 100.0 * i / Math.Max(1, source.Lines.Count)));
            }
            var line = source.Lines[i];
            if (!selected.Contains(line.ThreadId)) continue;
            indexes.Add(i);
            builder?.Append(line.RawText.Span).Append(line.LineEnding.Span);
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("필터 적용", 100));
        return new(source, indexes.ToArray(), all ? source.Text : builder!.ToString(), selected);
    }
}
