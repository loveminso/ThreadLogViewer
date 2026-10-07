using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ThreadLogViewer.Core;

public sealed record SavedHighlight(string Phrase, int Color, bool Enabled = true);
public sealed record SavedBookmark(int Line, string Label);
public sealed record SavedPosition(int Line, int Column, int TopLine, double TopDelta, double Horizontal);
public sealed record SavedNavigation(SavedPosition Position, int? ContextLine);
public sealed record SourceStamp(string Hash, int Characters, int Lines);

public sealed record AnalysisPreset
{
    public EntryFilter Filter { get; init; } = EntryFilter.Empty;
    public bool AllThreads { get; init; } = true;
    public int?[] Threads { get; init; } = [];
    public SavedHighlight[] Highlights { get; init; } = [];
}

public sealed record SavedAnalysis
{
    public SourceStamp Source { get; init; } = new("", 0, 0);
    public string? SourcePath { get; init; }
    public string? PastedText { get; init; }
    public EncodingMode Encoding { get; init; }
    public string Title { get; init; } = "분석 상태";
    public string? SourceTitle { get; init; }
    public LogLineRange? Scope { get; init; }
    public AnalysisPreset Preset { get; init; } = new();
    public SavedBookmark[] Bookmarks { get; init; } = [];
    public SavedPosition? Position { get; init; }
    public SavedPosition? EmptyPosition { get; init; }
    public SavedPosition? NormalPosition { get; init; }
    public SourceTextRange[] Selection { get; init; } = [];
    public int[]? WholeLines { get; init; }
    public string IncludesDraft { get; init; } = "";
    public string ExcludesDraft { get; init; } = "";
    public bool RequireAllDraft { get; init; }
    public bool FilterCaseDraft { get; init; }
    public string ThreadQuery { get; init; } = "";
    public string KeywordDraft { get; init; } = "";
    public int KeywordColor { get; init; }
    public string Query { get; init; } = "";
    public bool SearchCase { get; init; }
    public bool SearchWord { get; init; }
    public bool SearchRegex { get; init; }
    public int SearchScope { get; init; }
    public SourceTextRange[]? SearchRanges { get; init; }
    public LocatedSearchHit? LastHit { get; init; }
    public bool SearchVisible { get; init; }
    public int? ContextLine { get; init; }
    public int ContextRadius { get; init; } = 20;
    public int? TimeA { get; init; }
    public int? TimeB { get; init; }
    public int? SeparationStart { get; init; }
    public bool FilterExpanded { get; init; }
    public bool BookmarksExpanded { get; init; }
    public int AnalysisTool { get; init; }
    public SavedNavigation[] Back { get; init; } = [];
    public SavedNavigation[] Forward { get; init; } = [];
}

/// <summary>Explicit local files, independent of display settings. Saves create a new file only.</summary>
public static class AnalysisFiles
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private const string Owner = "ThreadLogViewer.Analysis";
    private sealed record Envelope<T>(string Owner, int Schema, string Kind, T Value);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, MaxDepth = 24 };

    public static SourceStamp Stamp(LogData source, CancellationToken token = default)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int start = 0; start < source.Text.Length; start += 65536)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(MemoryMarshal.AsBytes(source.Text.AsSpan(start, Math.Min(65536, source.Text.Length - start))));
        }
        token.ThrowIfCancellationRequested();
        return new(Convert.ToHexString(hash.GetHashAndReset()), source.Text.Length, source.Lines.Count);
    }

    public static Task SavePresetAsync(AnalysisPreset value, string path, string? sourcePath = null, CancellationToken token = default)
    { Validate(value); return SaveAsync(value, "preset", path, sourcePath, token); }
    public static Task SaveAnalysisAsync(SavedAnalysis value, string path, CancellationToken token = default)
    { Validate(value); return SaveAsync(value, "state", path, value.SourcePath, token); }
    public static async Task<AnalysisPreset> ReadPresetAsync(string path, CancellationToken token = default)
    { var value = await ReadAsync<AnalysisPreset>(path, "preset", token); Validate(value); return value; }
    public static async Task<SavedAnalysis> ReadAnalysisAsync(string path, CancellationToken token = default)
    { var value = await ReadAsync<SavedAnalysis>(path, "state", token); Validate(value); return value; }

    private const string SizeLimitMessage = "분석 파일은 최대 8 MiB입니다. 큰 붙여넣기는 먼저 로그로 내보낸 뒤 파일 탭에서 저장하세요.";

    private static async Task SaveAsync<T>(T value, string kind, string path, string? sourcePath, CancellationToken token,
        Func<string, Stream>? openStream = null)
    {
        token.ThrowIfCancellationRequested();
        LogExporter.ValidateDestination(sourcePath, path);
        if (value is SavedAnalysis { PastedText: { } text }) CheckPastedTextSize(text, token);
        string destination = Path.GetFullPath(path);
        string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".threadlog-analysis-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = openStream?.Invoke(temporary) ??
                new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                using var limited = new LimitedWriteStream(stream, token);
                await JsonSerializer.SerializeAsync(limited, new Envelope<T>(Owner, 1, kind, value), Json, token);
                await limited.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            LogExporter.ValidateDestination(sourcePath, destination);
            File.Move(temporary, destination, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // SerializeAsync may buffer one string before writing it. Bound that string's escaped
    // representation in small chunks first; the stream separately includes all other fields.
    private static void CheckPastedTextSize(string text, CancellationToken token)
    {
        if (text.Length > MaximumBytes) throw new InvalidDataException(SizeLimitMessage);
        long encodedBytes = 2; // JSON quotation marks.
        for (int start = 0; start < text.Length;)
        {
            token.ThrowIfCancellationRequested();
            int length = Math.Min(16384, text.Length - start);
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]) &&
                char.IsLowSurrogate(text[start + length])) length++;
            for (int index = start; index < start + length; index++)
            {
                if (char.IsHighSurrogate(text[index]))
                {
                    if (index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
                        throw new InvalidDataException("붙여넣은 원문에 복원할 수 없는 Unicode 문자가 있습니다. 원문 파일로 다시 열어 저장하세요.");
                    index++;
                }
                else if (char.IsLowSurrogate(text[index]))
                    throw new InvalidDataException("붙여넣은 원문에 복원할 수 없는 Unicode 문자가 있습니다. 원문 파일로 다시 열어 저장하세요.");
            }
            encodedBytes += JsonEncodedText.Encode(text.AsSpan(start, length), Json.Encoder).EncodedUtf8Bytes.Length;
            if (encodedBytes > MaximumBytes) throw new InvalidDataException(SizeLimitMessage);
            start += length;
        }
        token.ThrowIfCancellationRequested();
    }

    private sealed class LimitedWriteStream(Stream inner, CancellationToken token) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        private void Check(int count, CancellationToken callerToken = default)
        {
            token.ThrowIfCancellationRequested();
            callerToken.ThrowIfCancellationRequested();
            if (count > MaximumBytes - written) throw new InvalidDataException(SizeLimitMessage);
        }
        private void CheckCancellation(CancellationToken callerToken = default)
        {
            token.ThrowIfCancellationRequested();
            callerToken.ThrowIfCancellationRequested();
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            inner.Write(buffer);
            written += buffer.Length;
            CheckCancellation();
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Check(buffer.Length, cancellationToken);
            await inner.WriteAsync(buffer, cancellationToken);
            written += buffer.Length;
            CheckCancellation(cancellationToken);
        }
        public override void Flush() { CheckCancellation(); inner.Flush(); CheckCancellation(); }
        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            CheckCancellation(cancellationToken);
            await inner.FlushAsync(cancellationToken);
            CheckCancellation(cancellationToken);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        // The SaveAsync scope owns and closes the underlying temporary file.
    }

    private static async Task<T> ReadAsync<T>(string path, string kind, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("지원하는 분석 파일 크기는 1바이트~8 MiB입니다.");
        byte[] bytes = new byte[(int)stream.Length]; await stream.ReadExactlyAsync(bytes, token);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("Owner", out var owner) || owner.GetString() != Owner ||
            !root.TryGetProperty("Schema", out var schema) || !schema.TryGetInt32(out int version) || version != 1 ||
            !root.TryGetProperty("Kind", out var type) || type.GetString() != kind || !root.TryGetProperty("Value", out var value))
            throw new InvalidDataException("이 앱의 지원되는 분석 파일이 아닙니다. 현재 분석은 유지됩니다.");
        return value.Deserialize<T>(Json) ?? throw new InvalidDataException("분석 파일의 내용이 없습니다.");
    }

    public static void Validate(AnalysisPreset value)
    {
        if (value is null || value.Filter is null || value.Filter.Includes is null || value.Filter.Excludes is null ||
            value.Threads is null || value.Threads.Length > 100000 || value.Highlights is null || value.Highlights.Length > 8)
            throw new InvalidDataException("프리셋의 조건 또는 강조 규칙이 올바르지 않습니다.");
        foreach (var terms in new[] { value.Filter.Includes, value.Filter.Excludes })
            if (terms.Length > 256 || terms.Any(term => term is null || string.IsNullOrWhiteSpace(term) || term.Length > 4096))
                throw new InvalidDataException("필터 문구는 각각 4,096자·256개 이내여야 합니다.");
        if (value.Highlights.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Phrase) || rule.Phrase.Length > 4096 || rule.Color is < 0 or > 5))
            throw new InvalidDataException("강조 문구와 색이 올바르지 않습니다.");
    }

    public static void Validate(SavedAnalysis value)
    {
        if (value is null || value.Source is null || value.Source.Hash is null || value.Source.Hash.Length != 64 ||
            !value.Source.Hash.All(Uri.IsHexDigit) || value.Source.Characters < 0 || value.Source.Lines < 0 ||
            value.Encoding is not (EncodingMode.Auto or EncodingMode.Cp949) || value.Title is null || value.Title.Length > 1024 || value.SourceTitle?.Length > 1024 ||
            value.Query is null || value.Query.Length > 65536 || value.ThreadQuery is null || value.ThreadQuery.Length > 4096 ||
            value.IncludesDraft is null || value.IncludesDraft.Length > 1024 * 1024 || value.ExcludesDraft is null || value.ExcludesDraft.Length > 1024 * 1024 ||
            value.KeywordDraft is null || value.KeywordDraft.Length > 4096 || value.KeywordColor is < 0 or > 5 ||
            value.SearchScope is < 0 or > 2 || value.AnalysisTool is < 0 or > 2 || value.ContextRadius is < 0 or > 10000 ||
            value.Bookmarks is null || value.Bookmarks.Length > 100000 || value.Selection is null || value.Selection.Length > 100000 ||
            value.Back is null || value.Forward is null || value.Back.Length > 100 || value.Forward.Length > 100 ||
            value.SourcePath is null && value.PastedText is null)
            throw new InvalidDataException("분석 파일의 값 또는 원문 정보가 올바르지 않습니다.");
        Validate(value.Preset);
        if (value.SourcePath is { } sourcePath && (!Path.IsPathFullyQualified(sourcePath) || sourcePath.StartsWith("\\\\", StringComparison.Ordinal) || sourcePath.StartsWith("//", StringComparison.Ordinal)))
            throw new InvalidDataException("분석 상태는 로컬 원본 파일의 전체 경로만 자동으로 복원합니다. 로컬 파일을 열어 저장하세요.");
        bool Line(int line) => line >= 0 && line < value.Source.Lines && (value.Scope is null || value.Scope.Value.Contains(line));
        bool OptionalLine(int? line) => line is null || Line(line.Value);
        bool Position(SavedPosition? point) => point is null || Line(point.Line) && Line(point.TopLine) && point.Column >= 1 &&
            double.IsFinite(point.TopDelta) && point.TopDelta is >= 0 and <= 10000000 && double.IsFinite(point.Horizontal) && point.Horizontal >= 0;
        if (value.Scope is { } scope && (scope.FirstLineIndex < 0 || scope.LastLineIndex < scope.FirstLineIndex || scope.LastLineIndex >= value.Source.Lines) ||
            value.Bookmarks.Any(bookmark => bookmark is null || !Line(bookmark.Line) || bookmark.Label is null || bookmark.Label.Length > 4096) ||
            value.Bookmarks.Select(bookmark => bookmark.Line).Distinct().Count() != value.Bookmarks.Length ||
            !Position(value.Position) || !Position(value.EmptyPosition) || !Position(value.NormalPosition) ||
            !OptionalLine(value.ContextLine) || !OptionalLine(value.TimeA) || !OptionalLine(value.TimeB) || !OptionalLine(value.SeparationStart) ||
            value.TimeA is null && value.TimeB is not null || value.WholeLines?.Any(line => !Line(line)) == true ||
            value.LastHit is { } hit && (!Line(hit.SourceLineIndex) || hit.SourceOffset < 0 || hit.Length < 0 ||
                (long)hit.SourceOffset + hit.Length > value.Source.Characters || hit.SourceColumn < 1 || hit.EntryIndex < 0) ||
            value.Back.Concat(value.Forward).Any(point => point is null || point.Position is null || !Position(point.Position) || !OptionalLine(point.ContextLine)))
            throw new InvalidDataException("분석 위치가 원문 또는 세션 범위를 벗어납니다.");
        foreach (var range in value.Selection.Concat(value.SearchRanges ?? []))
            if (range.Offset < 0 || range.Length < 0 || (long)range.Offset + range.Length > value.Source.Characters)
                throw new InvalidDataException("선택 범위가 원문을 벗어납니다.");
    }
}
