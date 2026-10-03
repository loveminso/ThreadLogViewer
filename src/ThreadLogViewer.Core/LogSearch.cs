using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThreadLogViewer.Core;

public readonly record struct SearchHit(int Offset, int Length);
public sealed record SearchResults(SearchHit[] Hits, bool Limited);
public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false, bool UseRegex = false);
public readonly record struct SourceTextRange(int Offset, int Length);
// LineIndex is zero based; SourceColumn is the one-based UTF-16 document column.
public readonly record struct LocatedSearchHit(int SourceOffset, int Length, int SourceLineIndex, int SourceColumn, int EntryIndex);
public sealed record LocatedSearchResults(LocatedSearchHit[] Hits, bool Limited);

public static class LogSearch
{
    public const int MaxHighlights = 100_000;
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    public static LocatedSearchResults Find(LogData source, IEnumerable<int> entryIndexes, string query,
        SearchOptions options, IReadOnlyList<SourceTextRange>? ranges = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (query.Length == 0) return new([], false);
        Regex? regex = options.UseRegex
            ? new Regex(query, RegexOptions.CultureInvariant |
                (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase), RegexTimeout) : null;
        var selectedEntries = new List<int>();
        bool ordered = true;
        int previousIndex = -1;
        foreach (int index in entryIndexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index < 0 || index >= source.Entries.Count) throw new ArgumentOutOfRangeException(nameof(entryIndexes));
            if (index < previousIndex) ordered = false;
            if (index != previousIndex) selectedEntries.Add(index);
            previousIndex = index;
        }
        if (!ordered)
        {
            selectedEntries.Sort();
            int uniqueCount = 0;
            for (int i = 0; i < selectedEntries.Count; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (uniqueCount == 0 || selectedEntries[i] != selectedEntries[uniqueCount - 1]) selectedEntries[uniqueCount++] = selectedEntries[i];
            }
            selectedEntries.RemoveRange(uniqueCount, selectedEntries.Count - uniqueCount);
        }
        SourceTextRange[]? selection = ranges is null ? null : NormalizeRanges(ranges, source.Text.Length, cancellationToken);
        var hits = new List<LocatedSearchHit>();
        int rangeCursor = 0;
        foreach (int entryIndex in selectedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryRange = source.GetEntryRange(entryIndex);
            if (selection is null)
            {
                if (SearchRange(entryRange, entryRange, entryIndex)) return new(hits.ToArray(), true);
                continue;
            }
            int entryEnd = entryRange.Offset + entryRange.Length;
            while (rangeCursor < selection.Length && selection[rangeCursor].Offset + selection[rangeCursor].Length <= entryRange.Offset) rangeCursor++;
            for (int j = rangeCursor; j < selection.Length && selection[j].Offset < entryEnd; j++)
            {
                int start = Math.Max(entryRange.Offset, selection[j].Offset);
                int end = Math.Min(entryEnd, selection[j].Offset + selection[j].Length);
                if (end > start && SearchRange(new(start, end - start), entryRange, entryIndex)) return new(hits.ToArray(), true);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(hits.ToArray(), false);

        bool SearchRange(SourceTextRange searchRange, SourceTextRange entryRange, int entryIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var span = source.Text.AsSpan(searchRange.Offset, searchRange.Length);
            if (regex is not null)
            {
                foreach (var match in regex.EnumerateMatches(span))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (AddMatch(searchRange.Offset + match.Index, match.Length, entryRange, entryIndex)) return true;
                }
                return false;
            }
            var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int position = 0;
            while (position <= span.Length - query.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int length = (int)Math.Min(span.Length - (long)position, 65536L + query.Length - 1);
                int found = span.Slice(position, length).IndexOf(query, comparison);
                if (found < 0) { position += Math.Min(65536, span.Length - position); continue; }
                found += position;
                int absolute = searchRange.Offset + found;
                bool word = !options.WholeWord || HasWordBoundaries(source.Text, absolute, query.Length, entryRange);
                if (word && AddMatch(absolute, query.Length, entryRange, entryIndex)) return true;
                position = found + (word ? query.Length : 1);
            }
            return false;
        }

        bool AddMatch(int offset, int length, SourceTextRange entryRange, int entryIndex)
        {
            if (options.WholeWord && !HasWordBoundaries(source.Text, offset, length, entryRange)) return false;
            if (hits.Count == MaxHighlights) return true;
            int lineIndex = source.GetLineIndexAtOffset(offset);
            var entry = source.Entries[entryIndex];
            // A zero-width end match belongs to this record, never the next header or editor-only empty line.
            lineIndex = Math.Clamp(lineIndex, entry.StartLineIndex, entry.StartLineIndex + entry.LineCount - 1);
            int column = Math.Min(offset - source.GetLineOffset(lineIndex), source.Lines[lineIndex].RawText.Length) + 1;
            hits.Add(new(offset, length, lineIndex, column, entryIndex));
            return false;
        }
    }

    private static SourceTextRange[] NormalizeRanges(IReadOnlyList<SourceTextRange> ranges, int textLength, CancellationToken token)
    {
        var sorted = new List<SourceTextRange>();
        foreach (var range in ranges)
        {
            token.ThrowIfCancellationRequested();
            if (range.Offset < 0 || range.Length < 0 || range.Offset + (long)range.Length > textLength)
                throw new ArgumentOutOfRangeException(nameof(ranges));
            if (range.Length > 0) sorted.Add(range);
        }
        sorted.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        var merged = new List<SourceTextRange>();
        foreach (var range in sorted)
        {
            token.ThrowIfCancellationRequested();
            if (merged.Count == 0) { merged.Add(range); continue; }
            var previous = merged[^1];
            int end = previous.Offset + previous.Length;
            if (range.Offset > end) merged.Add(range);
            else merged[^1] = new(previous.Offset, Math.Max(end, range.Offset + range.Length) - previous.Offset);
        }
        return merged.ToArray();
    }

    private static bool HasWordBoundaries(string text, int offset, int length, SourceTextRange entry)
    {
        int end = offset + length;
        return (offset == entry.Offset || !IsWordBefore(text, offset)) &&
            (end == entry.Offset + entry.Length || !IsWordAt(text, end));
    }

    private static bool IsWordBefore(string text, int offset)
    {
        int index = offset - 1;
        if (index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])) index--;
        return IsWordAt(text, index);
    }

    private static bool IsWordAt(string text, int offset)
    {
        Rune rune;
        if (offset + 1 < text.Length && Rune.TryCreate(text[offset], text[offset + 1], out rune)) { }
        else if (!Rune.TryCreate(text[offset], out rune)) return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or
            UnicodeCategory.ConnectorPunctuation;
    }

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
