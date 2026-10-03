namespace ThreadLogViewer.Core;

public sealed record EntryFilter(string[] Includes, string[] Excludes, bool RequireAll = false, bool MatchCase = false)
{
    public static EntryFilter Empty { get; } = new([], []);

    internal PreparedEntryFilter Prepare() => new(Includes, Excludes, RequireAll, MatchCase);
}

internal sealed class PreparedEntryFilter
{
    private readonly string[] includes;
    private readonly string[] excludes;
    private readonly bool requireAll;
    private readonly StringComparison comparison;

    public PreparedEntryFilter(IEnumerable<string> includes, IEnumerable<string> excludes, bool requireAll, bool matchCase)
    {
        comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var comparer = matchCase ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        // Keep meaningful spaces inside and at either edge of a phrase.
        this.includes = includes.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(comparer).ToArray();
        this.excludes = excludes.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(comparer).ToArray();
        this.requireAll = requireAll;
    }

    public bool Matches(ReadOnlySpan<char> text, CancellationToken token)
    {
        foreach (string phrase in excludes)
        {
            if (Contains(text, phrase, token)) return false;
        }
        if (includes.Length == 0) return true;
        foreach (string phrase in includes)
        {
            bool found = Contains(text, phrase, token);
            if (requireAll && !found) return false;
            if (!requireAll && found) return true;
        }
        return requireAll;
    }

    private bool Contains(ReadOnlySpan<char> text, string phrase, CancellationToken token)
    {
        int position = 0;
        while (position <= text.Length - phrase.Length)
        {
            token.ThrowIfCancellationRequested();
            int length = (int)Math.Min(text.Length - (long)position, 65536L + phrase.Length - 1);
            if (text.Slice(position, length).IndexOf(phrase, comparison) >= 0) return true;
            position += Math.Min(65536, text.Length - position);
        }
        return false;
    }
}
