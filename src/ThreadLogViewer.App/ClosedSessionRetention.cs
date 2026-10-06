namespace ThreadLogViewer.App;

public readonly record struct RetainedResource(object Identity, long EstimatedBytes);

/// <summary>In-memory recovery budget counts shared resources once and excludes resources still owned by open tabs.</summary>
public sealed class ClosedSessionRetention<T>(Func<T, IEnumerable<RetainedResource>> resources,
    int maximumCount = 8, long maximumBytes = 128L * 1024 * 1024) where T : class
{
    private readonly List<(T Session, int Index)> entries = [];
    public int Count => entries.Count;
    public long EstimatedBytes { get; private set; }

    public bool Add(T session, int index, IEnumerable<T> openSessions, out int evicted)
    {
        var open = OpenResources(openSessions);
        evicted = 0;
        if (maximumCount <= 0 || Estimate([session], open) > maximumBytes)
        {
            evicted = TrimCore(open);
            return false;
        }
        entries.Add((session, index));
        evicted = TrimCore(open);
        return true;
    }

    public void Trim(IEnumerable<T> openSessions) => TrimCore(OpenResources(openSessions));

    public bool TryPop(out T? session, out int index)
    {
        if (entries.Count == 0) { session = null; index = -1; return false; }
        (session, index) = entries[^1]; entries.RemoveAt(entries.Count - 1);
        if (entries.Count == 0) EstimatedBytes = 0;
        return true;
    }

    public void Clear() { entries.Clear(); EstimatedBytes = 0; }

    private HashSet<object> OpenResources(IEnumerable<T> openSessions) =>
        openSessions.SelectMany(resources).Select(resource => resource.Identity).ToHashSet(ReferenceEqualityComparer.Instance);

    private int TrimCore(HashSet<object> open)
    {
        int evicted = 0;
        EstimatedBytes = Estimate(entries.Select(entry => entry.Session), open);
        while (entries.Count > maximumCount || EstimatedBytes > maximumBytes)
        {
            entries.RemoveAt(0); evicted++;
            EstimatedBytes = Estimate(entries.Select(entry => entry.Session), open);
        }
        return evicted;
    }

    private long Estimate(IEnumerable<T> sessions, HashSet<object> open)
    {
        var seen = new HashSet<object>(open, ReferenceEqualityComparer.Instance);
        long sum = 0;
        foreach (var resource in sessions.SelectMany(resources))
            if (seen.Add(resource.Identity)) sum = checked(sum + Math.Max(0, resource.EstimatedBytes));
        return sum;
    }
}
