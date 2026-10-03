using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

/// <summary>Maps selected display text back to source slices without joining filtered-out lines.</summary>
public static class SelectionSourceRanges
{
    public static IReadOnlyList<SourceTextRange> Map(LogProjection projection,
        IEnumerable<SelectionSpan> segments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selections = segments.Select(s =>
        {
            if (s.Offset < 0 || s.Length < 0 || (long)s.Offset + s.Length > projection.Text.Length)
                throw new ArgumentOutOfRangeException(nameof(segments));
            return s;
        }).Where(s => s.Length > 0).OrderBy(s => s.Offset).ToArray();
        var mapped = new List<SourceTextRange>();
        for (int selectionIndex = 0; selectionIndex < selections.Length; selectionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selection = selections[selectionIndex];
            int end = selection.Offset + selection.Length;
            while (selectionIndex + 1 < selections.Length && selections[selectionIndex + 1].Offset <= end)
            {
                var next = selections[++selectionIndex];
                end = Math.Max(end, next.Offset + next.Length);
            }
            int firstSourceOffset = projection.GetSourceOffset(selection.Offset)!.Value;
            int firstSourceLine = projection.Source.GetLineIndexAtOffset(firstSourceOffset);
            int displayLineIndex = projection.FindDisplayLine(firstSourceLine + 1)!.Value - 1;
            int displayLineOffset = selection.Offset - firstSourceOffset + projection.Source.GetLineOffset(firstSourceLine);
            while (displayLineIndex < projection.Count && displayLineOffset < end)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int sourceLineIndex = projection.SourceIndexes[displayLineIndex];
                var line = projection.Source.Lines[sourceLineIndex];
                int lineEnd = displayLineOffset + line.RawText.Length + line.LineEnding.Length;
                int selectedStart = Math.Max(selection.Offset, displayLineOffset);
                int selectedEnd = Math.Min(end, lineEnd);
                if (selectedEnd > selectedStart)
                {
                    int sourceOffset = projection.Source.GetLineOffset(sourceLineIndex) + selectedStart - displayLineOffset;
                    int length = selectedEnd - selectedStart;
                    if (mapped.Count > 0 && sourceOffset <= mapped[^1].Offset + mapped[^1].Length)
                    {
                        var previous = mapped[^1];
                        mapped[^1] = new(previous.Offset, Math.Max(previous.Offset + previous.Length, sourceOffset + length) - previous.Offset);
                    }
                    else mapped.Add(new(sourceOffset, length));
                }
                displayLineOffset = lineEnd;
                displayLineIndex++;
            }
        }
        return mapped.AsReadOnly();
    }
}
