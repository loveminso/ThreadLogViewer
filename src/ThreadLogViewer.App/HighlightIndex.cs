namespace ThreadLogViewer.App;

public readonly record struct HighlightSpan(int Offset, int Length, int RuleIndex);

/// <summary>Prefix maximum ends retain long overlaps even when later short spans end first.</summary>
public sealed class HighlightIndex
{
    private readonly HighlightSpan[] spans;
    private readonly int[] prefixMaxEnd;
    public static HighlightIndex Empty { get; } = new([]);
    public int Count => spans.Length;
    public IReadOnlyList<HighlightSpan> Spans => Array.AsReadOnly(spans);

    public HighlightIndex(IEnumerable<HighlightSpan> source)
    {
        spans = source.Where(s => s.Length > 0).OrderBy(s => s.Offset).ThenBy(s => s.RuleIndex).ToArray();
        prefixMaxEnd = new int[spans.Length];
        int maximum = 0;
        for (int i = 0; i < spans.Length; i++)
        {
            if (spans[i].Offset < 0 || (long)spans[i].Offset + spans[i].Length > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(source));
            maximum = Math.Max(maximum, spans[i].Offset + spans[i].Length);
            prefixMaxEnd[i] = maximum;
        }
    }

    /// <summary>Returns clipped spans intersecting the viewport [start,end), in start order.</summary>
    public IEnumerable<HighlightSpan> Query(int start, int end)
    {
        if (start < 0 || end < start) throw new ArgumentOutOfRangeException(nameof(start));
        if (start == end) yield break;
        int low = 0, high = spans.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (prefixMaxEnd[mid] <= start) low = mid + 1; else high = mid;
        }
        for (int i = low; i < spans.Length && spans[i].Offset < end; i++)
        {
            int clippedStart = Math.Max(start, spans[i].Offset);
            int clippedEnd = Math.Min(end, spans[i].Offset + spans[i].Length);
            if (clippedStart < clippedEnd) yield return new(clippedStart, clippedEnd - clippedStart, spans[i].RuleIndex);
        }
    }
}
