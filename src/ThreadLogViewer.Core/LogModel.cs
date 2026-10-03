namespace ThreadLogViewer.Core;

public enum ParseQuality { Complete, Partial, Unrecognized, Continuation }
public enum EncodingMode { Auto, Cp949 }

// Slices share the immutable decoded file string; filtering does not clone parsed fields.
// ThreadId is the owning entry's ID. TimeOfDay is an input duration and may exceed 24 hours.
// Continuation lines do not invent their own timestamp/source fields.
public readonly record struct LogLine(
    int OriginalLineNumber, ReadOnlyMemory<char> RawText, ReadOnlyMemory<char> LineEnding,
    int? ThreadId, TimeSpan? TimeOfDay, ReadOnlyMemory<char> TimestampText,
    ReadOnlyMemory<char> Message, ReadOnlyMemory<char> SourceFile, int? SourceLineNumber,
    ParseQuality Quality, int EntryIndex = 0);

// One timestamp header and its following physical lines, or one pre-header line.
public readonly record struct LogEntry(int StartLineIndex, int LineCount, int? ThreadId);
public sealed record ThreadSummary(int? ThreadId, int Count, string? FirstRecordedTime, string? LastRecordedTime,
    int EntryCount);
public sealed record WorkProgress(string Phase, double Percent);

public sealed class LogData(string? sourcePath, string text, string encodingDescription,
    LogLine[] lines, LogEntry[] entries, IReadOnlyList<ThreadSummary> threads)
{
    private readonly int[] lineOffsets = BuildLineOffsets(lines);
    // Null means text supplied directly by the user, with no known source file to re-read.
    public string? SourcePath { get; } = sourcePath;
    public string Text { get; } = text;
    public string EncodingDescription { get; } = encodingDescription;
    public IReadOnlyList<LogLine> Lines { get; } = Array.AsReadOnly(lines);
    public IReadOnlyList<LogEntry> Entries { get; } = Array.AsReadOnly(entries);
    public IReadOnlyList<ThreadSummary> Threads { get; } = threads;
    public int CompleteCount { get; } = lines.Count(l => l.Quality == ParseQuality.Complete);
    public int PartialCount { get; } = lines.Count(l => l.Quality == ParseQuality.Partial);
    public int UnrecognizedCount => Entries.Count - CompleteCount - PartialCount;
    public int ContinuationCount => Lines.Count - Entries.Count;

    // The end sentinel is useful for the final entry; it is not an extra source line.
    public int GetLineOffset(int sourceLineIndex) => sourceLineIndex >= 0 && sourceLineIndex < lineOffsets.Length
        ? lineOffsets[sourceLineIndex] : throw new ArgumentOutOfRangeException(nameof(sourceLineIndex));

    public SourceTextRange GetEntryRange(int entryIndex)
    {
        if (entryIndex < 0 || entryIndex >= Entries.Count) throw new ArgumentOutOfRangeException(nameof(entryIndex));
        var entry = Entries[entryIndex];
        int start = GetLineOffset(entry.StartLineIndex);
        return new(start, GetLineOffset(entry.StartLineIndex + entry.LineCount) - start);
    }

    public int GetLineIndexAtOffset(int sourceOffset)
    {
        if (sourceOffset < 0 || sourceOffset > Text.Length) throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        if (Lines.Count == 0) return -1;
        int found = Array.BinarySearch(lineOffsets, 0, Lines.Count, sourceOffset);
        return found >= 0 ? found : Math.Max(0, ~found - 1);
    }

    private static int[] BuildLineOffsets(LogLine[] lines)
    {
        var offsets = new int[lines.Length + 1];
        for (int i = 0; i < lines.Length; i++)
            offsets[i + 1] = checked(offsets[i] + lines[i].RawText.Length + lines[i].LineEnding.Length);
        return offsets;
    }
}

public static class ThreadColors
{
    // Stable palette: based on the numeric ID, never on ordering or string.GetHashCode().
    private static readonly string[] Palette = ["#E8EFF9", "#E9F3E8", "#F8ECE3", "#F0EAF7",
        "#E3F2F1", "#F7E8EC", "#F3F0DD", "#E8ECF0", "#EBF1DA", "#E7EFF4", "#F3E6F0", "#F2EBE6"];
    public static string ForThread(int? id) => id is null ? "#F2F2F2" : Palette[(uint)id.Value % Palette.Length];
}
