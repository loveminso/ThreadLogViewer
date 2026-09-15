namespace ThreadLogViewer.Core;

public enum ParseQuality { Complete, Partial, Unrecognized }
public enum EncodingMode { Auto, Cp949 }

// Slices share the immutable decoded file string; filtering does not clone parsed fields.
public readonly record struct LogLine(
    int OriginalLineNumber, ReadOnlyMemory<char> RawText, ReadOnlyMemory<char> LineEnding,
    int? ThreadId, TimeSpan? TimeOfDay, ReadOnlyMemory<char> TimestampText,
    ReadOnlyMemory<char> Message, ReadOnlyMemory<char> SourceFile, int? SourceLineNumber,
    ParseQuality Quality);

public sealed record ThreadSummary(int? ThreadId, int Count, string? FirstRecordedTime, string? LastRecordedTime);
public sealed record WorkProgress(string Phase, double Percent);

public sealed class LogData(string? sourcePath, string text, string encodingDescription,
    LogLine[] lines, IReadOnlyList<ThreadSummary> threads)
{
    // Null means text supplied directly by the user, with no known source file to re-read.
    public string? SourcePath { get; } = sourcePath;
    public string Text { get; } = text;
    public string EncodingDescription { get; } = encodingDescription;
    public IReadOnlyList<LogLine> Lines { get; } = Array.AsReadOnly(lines);
    public IReadOnlyList<ThreadSummary> Threads { get; } = threads;
    public int CompleteCount { get; } = lines.Count(l => l.Quality == ParseQuality.Complete);
    public int PartialCount { get; } = lines.Count(l => l.Quality == ParseQuality.Partial);
    public int UnrecognizedCount => Lines.Count - CompleteCount - PartialCount;
}

public static class ThreadColors
{
    // Stable palette: based on the numeric ID, never on ordering or string.GetHashCode().
    private static readonly string[] Palette = ["#E8EFF9", "#E9F3E8", "#F8ECE3", "#F0EAF7",
        "#E3F2F1", "#F7E8EC", "#F3F0DD", "#E8ECF0", "#EBF1DA", "#E7EFF4", "#F3E6F0", "#F2EBE6"];
    public static string ForThread(int? id) => id is null ? "#F2F2F2" : Palette[(uint)id.Value % Palette.Length];
}
