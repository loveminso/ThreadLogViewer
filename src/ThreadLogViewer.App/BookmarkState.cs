namespace ThreadLogViewer.App;

public sealed record Bookmark(int SourceLineIndex, string Label);

/// <summary>Session bookmarks use original physical lines, independent of the current filter.</summary>
public sealed class BookmarkState
{
    private readonly List<Bookmark> items = [];
    public IReadOnlyList<Bookmark> Items => items.AsReadOnly();

    public bool Toggle(int sourceLineIndex, string label = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLineIndex);
        int index = Find(sourceLineIndex);
        if (index >= 0) { items.RemoveAt(index); return false; }
        items.Insert(~index, new(sourceLineIndex, label.Trim()));
        return true;
    }

    public bool SetLabel(int sourceLineIndex, string label)
    {
        int index = Find(sourceLineIndex);
        if (index < 0) return false;
        items[index] = items[index] with { Label = label.Trim() };
        return true;
    }

    public Bookmark? Next(int sourceLineIndex)
    {
        if (items.Count == 0) return null;
        int index = Find(sourceLineIndex);
        int next = index >= 0 ? index + 1 : ~index;
        return items[next == items.Count ? 0 : next];
    }

    public Bookmark? Previous(int sourceLineIndex)
    {
        if (items.Count == 0) return null;
        int index = Find(sourceLineIndex);
        int previous = index >= 0 ? index - 1 : ~index - 1;
        return items[previous < 0 ? items.Count - 1 : previous];
    }

    public void Clear() => items.Clear();

    private int Find(int sourceLineIndex)
    {
        int low = 0, high = items.Count - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            int value = items[mid].SourceLineIndex;
            if (value == sourceLineIndex) return mid;
            if (value < sourceLineIndex) low = mid + 1; else high = mid - 1;
        }
        return ~low;
    }
}
