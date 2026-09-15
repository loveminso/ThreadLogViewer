using System.Globalization;
using System.Text.RegularExpressions;

namespace ThreadLogViewer.Core;

public static partial class LogParser
{
    [GeneratedRegex(@"^\s*\[(?<time>\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)\]", RegexOptions.CultureInvariant)]
    private static partial Regex TimePattern();
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
        var lines = new List<LogLine>();
        var summaries = new Dictionary<int, SummaryBuilder>();
        int position = 0;
        while (position < text.Length)
        {
            if ((lines.Count & 2047) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new("파싱", 100.0 * position / Math.Max(1, text.Length)));
            }
            int end = position;
            while (end < text.Length && text[end] is not '\r' and not '\n') end++;
            int next = end;
            if (next < text.Length && text[next++] == '\r' && next < text.Length && text[next] == '\n') next++;
            var line = ParseLine(text.AsMemory(position, end - position), text.AsMemory(end, next - end), lines.Count + 1);
            lines.Add(line);
            int key = line.ThreadId ?? -1;
            if (!summaries.TryGetValue(key, out var summary)) summaries[key] = summary = new();
            summary.Count++;
            if (line.TimeOfDay.HasValue)
            {
                summary.First ??= line.TimestampText.ToString();
                summary.Last = line.TimestampText.ToString();
            }
            position = next;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var threads = summaries.OrderBy(p => p.Key == -1 ? long.MaxValue : p.Key)
            .Select(p => new ThreadSummary(p.Key == -1 ? null : p.Key, p.Value.Count, p.Value.First, p.Value.Last)).ToArray();
        progress?.Report(new("파싱", 100));
        return new(sourcePath, text, encoding, lines.ToArray(), threads);
    }

    public static LogLine ParseLine(ReadOnlyMemory<char> raw, ReadOnlyMemory<char> ending, int lineNumber)
    {
        // Regex works on one transient line string. Stored field slices retain only the shared input.
        string value = raw.ToString();
        int prefixEnd = 0;
        TimeSpan? time = null;
        ReadOnlyMemory<char> stamp = default, source = default;
        int? thread = null, sourceLine = null;
        var tm = TimePattern().Match(value);
        if (tm.Success)
        {
            var g = tm.Groups["time"];
            stamp = raw.Slice(g.Index, g.Length);
            if (TimeOnly.TryParseExact(g.ValueSpan, ["HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedTime)) time = parsedTime.ToTimeSpan();
            prefixEnd = tm.Length;
        }
        else
        {
            // Skip an invalid leading timestamp-shaped bracket to recover an explicit T field.
            int close = value.IndexOf(']');
            if (value.TrimStart().StartsWith('[') && close >= 0 && value.AsSpan(0, close).Contains(':')) prefixEnd = close + 1;
        }
        var th = ThreadPattern().Match(value, prefixEnd);
        if (th.Success && int.TryParse(th.Groups["id"].ValueSpan, out int id))
        {
            thread = id;
            prefixEnd = th.Index + th.Length;
        }
        var sm = SourcePattern().Match(value);
        int messageEnd = value.Length;
        if (sm.Success && int.TryParse(sm.Groups["line"].ValueSpan, out int number) && number > 0)
        {
            var file = sm.Groups["file"];
            source = raw.Slice(file.Index, file.Length);
            sourceLine = number;
            messageEnd = sm.Index;
        }
        int messageStart = Math.Min(prefixEnd, messageEnd);
        while (messageStart < messageEnd && char.IsWhiteSpace(value[messageStart])) messageStart++;
        var quality = time.HasValue && thread.HasValue && sourceLine.HasValue ? ParseQuality.Complete :
            time.HasValue || thread.HasValue || sourceLine.HasValue ? ParseQuality.Partial : ParseQuality.Unrecognized;
        return new(lineNumber, raw, ending, thread, time, stamp, raw.Slice(messageStart, messageEnd - messageStart),
            source, sourceLine, quality);
    }

    private sealed class SummaryBuilder { public int Count; public string? First; public string? Last; }
}
