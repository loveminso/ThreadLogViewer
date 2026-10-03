using System.Globalization;

namespace ThreadLogViewer.Core;

public sealed record TimeAnchor(int SourceLineIndex, int HeaderLineIndex, int EntryIndex, long Ticks, string TimestampText);

public static class LogTimeAnalysis
{
    public static TimeAnchor? ResolveAnchor(LogData source, int sourceLineIndex)
    {
        if (sourceLineIndex < 0 || sourceLineIndex >= source.Lines.Count) return null;
        int entryIndex = source.Lines[sourceLineIndex].EntryIndex;
        int headerIndex = source.Entries[entryIndex].StartLineIndex;
        var header = source.Lines[headerIndex];
        return header.TimeOfDay is { } time
            ? new(sourceLineIndex, headerIndex, entryIndex, time.Ticks, header.TimestampText.ToString()) : null;
    }

    public static long DifferenceTicks(TimeAnchor a, TimeAnchor b) => checked(b.Ticks - a.Ticks);

    public static string FormatTicks(long ticks)
    {
        // Unsigned magnitude also safely handles long.MinValue when called independently.
        ulong magnitude = ticks < 0 ? (ulong)(-(ticks + 1)) + 1 : (ulong)ticks;
        ulong hours = magnitude / (ulong)TimeSpan.TicksPerHour;
        ulong minutes = magnitude / (ulong)TimeSpan.TicksPerMinute % 60;
        ulong seconds = magnitude / (ulong)TimeSpan.TicksPerSecond % 60;
        string fraction = (magnitude % (ulong)TimeSpan.TicksPerSecond).ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
        if (fraction.Length < 3) fraction = fraction.PadRight(3, '0');
        string sign = ticks < 0 ? "−" : "+";
        string milliseconds = (ticks / (decimal)TimeSpan.TicksPerMillisecond).ToString("0.####", CultureInfo.InvariantCulture);
        return $"{sign}{hours.ToString("D2", CultureInfo.InvariantCulture)}:{minutes.ToString("D2", CultureInfo.InvariantCulture)}:{seconds.ToString("D2", CultureInfo.InvariantCulture)}.{fraction} · {milliseconds} ms";
    }
}
