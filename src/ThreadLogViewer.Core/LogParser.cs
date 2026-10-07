using System.Globalization;
using System.Text.RegularExpressions;

namespace ThreadLogViewer.Core;

public static partial class LogParser
{
    // A malformed timestamp-shaped header still ends the previous entry.
    [GeneratedRegex(@"^\s*\[(?<time>[^:\]\r\n]+:[^:\]\r\n]+:[^\]\r\n]+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();
    [GeneratedRegex(@"\A[0-9]{2,}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimeValuePattern();
    [GeneratedRegex(@"\G\s*\[T\s*(?<id>\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex ThreadPattern();
    [GeneratedRegex(@"\s*\((?<file>[^()\r\n]+):(?<line>\d+)\)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourcePattern();

    public static LogData ParsePastedText(string text, CancellationToken cancellationToken = default,
        IProgress<WorkProgress>? progress = null) =>
        Parse(text, null, "클립보드 Unicode · 원본 인코딩 알 수 없음", cancellationToken, progress);

    public static LogData Parse(string text, string? sourcePath = "synthetic.log", string encoding = "테스트 입력",
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null)
    {
        // Count physical lines first so a large parse does not retain an oversized List<T>
        // backing array alongside an equally large final LogLine array during publication.
        int physicalCount = CountPhysicalLines(text, cancellationToken);
        var lines = new LogLine[physicalCount];
        var entries = new LogEntry[physicalCount];
        int lineCount = 0, entryCount = 0;
        var summaries = new Dictionary<int, SummaryBuilder>();
        int position = 0;
        int activeTimestampEntry = -1;
        while (position < text.Length)
        {
            if ((lineCount & 2047) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new("파싱", 100.0 * position / Math.Max(1, text.Length)));
            }
            int end = FindLineEnd(text, position, cancellationToken);
            int next = end;
            if (next < text.Length && text[next++] == '\r' && next < text.Length && text[next] == '\n') next++;
            var raw = text.AsMemory(position, end - position);
            var ending = text.AsMemory(end, next - end);
            bool timestamp = TimePattern().IsMatch(raw.Span);
            LogLine line;
            if (timestamp || activeTimestampEntry < 0)
            {
                int entryIndex = entryCount++;
                line = ParseHeader(raw, ending, lineCount + 1) with { EntryIndex = entryIndex };
                entries[entryIndex] = new(lineCount, 1, line.ThreadId);
                if (timestamp) activeTimestampEntry = entryIndex;
            }
            else
            {
                var entry = entries[activeTimestampEntry];
                entries[activeTimestampEntry] = entry with { LineCount = entry.LineCount + 1 };
                // Ownership follows the user's timestamp boundary rule. Do not parse dump contents as new headers.
                line = new(lineCount + 1, raw, ending, entry.ThreadId, null, default, raw, default, null,
                    ParseQuality.Continuation, activeTimestampEntry);
            }
            lines[lineCount++] = line;
            int key = line.ThreadId ?? -1;
            if (!summaries.TryGetValue(key, out var summary)) summaries[key] = summary = new();
            summary.Count++;
            if (line.Quality != ParseQuality.Continuation) summary.EntryCount++;
            if (line.TimeOfDay.HasValue)
            {
                summary.First ??= line.TimestampText;
                summary.Last = line.TimestampText;
            }
            position = next;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var threads = summaries.OrderBy(p => p.Key == -1 ? long.MaxValue : p.Key)
            .Select(p => new ThreadSummary(p.Key == -1 ? null : p.Key, p.Value.Count,
                p.Value.First?.ToString(), p.Value.Last?.ToString(), p.Value.EntryCount)).ToArray();
        progress?.Report(new("파싱", 100));
        if (entryCount != entries.Length) Array.Resize(ref entries, entryCount);
        return new(sourcePath, text, encoding, lines, entries, threads);
    }

    private static int CountPhysicalLines(string text, CancellationToken token)
    {
        int count = 0, position = 0;
        while (position < text.Length)
        {
            if ((count & 2047) == 0) token.ThrowIfCancellationRequested();
            int end = FindLineEnd(text, position, token);
            position = end;
            if (position < text.Length && text[position++] == '\r' && position < text.Length && text[position] == '\n') position++;
            count++;
        }
        token.ThrowIfCancellationRequested();
        return count;
    }

    private static int FindLineEnd(string text, int start, CancellationToken token)
    {
        const int block = 65536;
        while (start < text.Length)
        {
            token.ThrowIfCancellationRequested();
            int length = Math.Min(block, text.Length - start);
            int found = text.AsSpan(start, length).IndexOfAny('\r', '\n');
            if (found >= 0) return start + found;
            start += length;
        }
        return text.Length;
    }

    public static LogLine ParseLine(ReadOnlyMemory<char> raw, ReadOnlyMemory<char> ending, int lineNumber)
    {
        return ParseHeader(raw, ending, lineNumber);
    }

    private static LogLine ParseHeader(ReadOnlyMemory<char> raw, ReadOnlyMemory<char> ending, int lineNumber)
    {
        ReadOnlySpan<char> value = raw.Span;
        int prefixEnd = 0;
        TimeSpan? time = null;
        ReadOnlyMemory<char> stamp = default, source = default;
        int? thread = null, sourceLine = null;
        var timestamps = TimePattern().EnumerateMatches(value);
        if (timestamps.MoveNext())
        {
            var tm = timestamps.Current;
            int first = value[..tm.Length].IndexOf('[') + 1;
            stamp = raw.Slice(first, tm.Length - first - 1);
            time = ParseTimestamp(stamp.Span);
            prefixEnd = tm.Length;
        }
        var threads = ThreadPattern().EnumerateMatches(value, prefixEnd);
        if (threads.MoveNext())
        {
            var th = threads.Current;
            int first = th.Index;
            while (char.IsWhiteSpace(value[first])) first++;
            first += 2; // opening bracket and the literal T
            while (char.IsWhiteSpace(value[first])) first++;
            if (int.TryParse(value.Slice(first, th.Index + th.Length - first - 1), out int id))
            { thread = id; prefixEnd = th.Index + th.Length; }
        }
        int messageEnd = value.Length;
        var sources = SourcePattern().EnumerateMatches(value);
        if (sources.MoveNext())
        {
            var sm = sources.Current;
            int first = sm.Index;
            while (char.IsWhiteSpace(value[first])) first++;
            first++; // opening parenthesis
            int last = sm.Index + sm.Length - 1;
            while (char.IsWhiteSpace(value[last])) last--;
            int colon = first + value.Slice(first, last - first).LastIndexOf(':');
            if (int.TryParse(value.Slice(colon + 1, last - colon - 1), out int number) && number > 0)
            {
                source = raw.Slice(first, colon - first);
                sourceLine = number; messageEnd = sm.Index;
            }
        }
        int messageStart = Math.Min(prefixEnd, messageEnd);
        while (messageStart < messageEnd && char.IsWhiteSpace(value[messageStart])) messageStart++;
        var quality = time.HasValue && thread.HasValue && sourceLine.HasValue ? ParseQuality.Complete :
            time.HasValue || thread.HasValue || sourceLine.HasValue ? ParseQuality.Partial : ParseQuality.Unrecognized;
        return new(lineNumber, raw, ending, thread, time, stamp, raw.Slice(messageStart, messageEnd - messageStart),
            source, sourceLine, quality);
    }

    private static TimeSpan? ParseTimestamp(ReadOnlySpan<char> value)
    {
        if (!TimeValuePattern().IsMatch(value)) return null;
        int colon = value.IndexOf(':');
        if (!long.TryParse(value[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out long hours)) return null;
        int minutes = int.Parse(value.Slice(colon + 1, 2), CultureInfo.InvariantCulture);
        int seconds = int.Parse(value.Slice(colon + 4, 2), CultureInfo.InvariantCulture);
        if (minutes > 59 || seconds > 59) return null;
        int fractionStart = colon + 7;
        long fractionTicks = fractionStart < value.Length
            ? int.Parse(value[fractionStart..], CultureInfo.InvariantCulture) : 0;
        for (int digits = value.Length - fractionStart; digits > 0 && digits < 7; digits++) fractionTicks *= 10;
        try { return TimeSpan.FromTicks(checked(hours * TimeSpan.TicksPerHour + minutes * TimeSpan.TicksPerMinute + seconds * TimeSpan.TicksPerSecond + fractionTicks)); }
        catch (OverflowException) { return null; }
    }

    private sealed class SummaryBuilder { public int Count; public int EntryCount; public ReadOnlyMemory<char>? First; public ReadOnlyMemory<char>? Last; }
}
