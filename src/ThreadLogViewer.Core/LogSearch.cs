namespace ThreadLogViewer.Core;

public readonly record struct SearchHit(int Offset, int Length);
public sealed record SearchResults(SearchHit[] Hits, bool Limited);

public static class LogSearch
{
    public const int MaxHighlights = 100_000;
    public static SearchResults Find(string text, string query, CancellationToken cancellationToken = default)
    {
        if (query.Length == 0) return new([], false);
        var hits = new List<SearchHit>();
        int position = 0;
        while (position <= text.Length - query.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Bound individual IndexOf calls so a long no-match search remains cancellable.
            int chunkLength = (int)Math.Min(text.Length - (long)position, 65536L + query.Length - 1);
            int found = text.IndexOf(query, position, chunkLength, StringComparison.OrdinalIgnoreCase);
            if (found < 0) { position += Math.Min(65536, text.Length - position); if (position == text.Length) break; continue; }
            if (hits.Count == MaxHighlights) return new(hits.ToArray(), true);
            hits.Add(new(found, query.Length));
            position = found + query.Length;
        }
        return new(hits.ToArray(), false);
    }
}
