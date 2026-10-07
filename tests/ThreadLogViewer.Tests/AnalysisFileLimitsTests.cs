using System.IO;
using System.Reflection;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class AnalysisFileLimitsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnpairedSurrogateIsRejectedBeforeWritingAnUnrestorableState(bool high)
    {
        string text = "synthetic" + (high ? '\uD800' : '\uDC00');
        using var folder = new SyntheticFolder(); string path = folder.File("invalid-unicode.json");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisFiles.SaveAnalysisAsync(State(text), path));
        Assert.Contains("Unicode", error.Message);
        Assert.False(File.Exists(path)); Assert.Empty(Directory.GetFiles(folder.Path));
    }
    [Fact]
    public async Task OversizedPastedTextIsRejectedWithoutCreatingDestinationOrTemporaryFile()
    {
        using var folder = new SyntheticFolder();
        string path = folder.File("oversized-state.json");
        var state = State(new string('x', AnalysisFiles.MaximumBytes + 1));

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisFiles.SaveAnalysisAsync(state, path));

        Assert.Contains("8 MiB", error.Message);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task EscapedPastedTextExceedingJsonLimitIsRejectedBeforeOpeningTemporaryStream()
    {
        using var folder = new SyntheticFolder();
        string path = folder.File("escaped-state.json");
        // Each control character needs six UTF-8 bytes as a JSON escape.
        string text = new('\u0001', AnalysisFiles.MaximumBytes / 6 + 1);
        var state = State(text);
        AnalysisFiles.Validate(state);
        int streamsOpened = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => SaveWithStream(state, path, CancellationToken.None, temporary =>
        {
            streamsOpened++;
            return CreateTemporary(temporary);
        }));

        Assert.True(text.Length < AnalysisFiles.MaximumBytes);
        Assert.Equal(0, streamsOpened);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task TotalEscapedJsonSizeIsLimitedEvenWhenPastedTextPassesPreflight()
    {
        using var folder = new SyntheticFolder();
        string path = folder.File("large-drafts.json");
        // Both drafts are individually valid, but their escaped JSON exceeds 8 MiB.
        string draft = new('\uAC00', 1024 * 1024);
        var state = State("synthetic body") with { IncludesDraft = draft, ExcludesDraft = draft };
        AnalysisFiles.Validate(state);
        int streamsOpened = 0;
        long bytesWritten = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() => SaveWithStream(state, path, CancellationToken.None, temporary =>
        {
            streamsOpened++;
            return new ObservedWriteStream(CreateTemporary(temporary), count => bytesWritten += count);
        }));

        Assert.Equal(1, streamsOpened);
        Assert.InRange(bytesWritten, 0, AnalysisFiles.MaximumBytes);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public async Task SurrogatePairAtPreflightChunkBoundaryRoundTripsWithinAllowedSize()
    {
        using var folder = new SyntheticFolder();
        string path = folder.File("surrogate-boundary.json");
        // The high surrogate occupies the last position of a 16,384-char chunk.
        string text = new string('a', 16383) + "\U0001F642\r\nsynthetic 한글\n" + new string('b', 32768);
        var source = LogParser.ParsePastedText(text);
        var state = new SavedAnalysis { Source = AnalysisFiles.Stamp(source), PastedText = text };

        await AnalysisFiles.SaveAnalysisAsync(state, path);
        var loaded = await AnalysisFiles.ReadAnalysisAsync(path);

        Assert.Equal(text, loaded.PastedText);
        Assert.Equal(state.Source, loaded.Source);
        Assert.True(char.IsHighSurrogate(loaded.PastedText![16383]));
        Assert.True(char.IsLowSurrogate(loaded.PastedText[16384]));
        Assert.InRange(new FileInfo(path).Length, 1, AnalysisFiles.MaximumBytes);
        Assert.Empty(Directory.GetFiles(folder.Path, ".threadlog-analysis-*.tmp"));
    }

    [Fact]
    public async Task CancellationAfterWritingActualTemporaryBytesCleansUpAndNeverPublishesDestination()
    {
        using var folder = new SyntheticFolder();
        using var cancelled = new CancellationTokenSource();
        string path = folder.File("cancelled-during-write.json");
        var state = State(new string('x', 65536));
        string? temporaryPath = null;
        long bytesWritten = 0;
        bool temporaryExistedDuringWrite = false;
        int streamsOpened = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SaveWithStream(state, path, cancelled.Token, temporary =>
        {
            streamsOpened++;
            temporaryPath = temporary;
            return new ObservedWriteStream(CreateTemporary(temporary), count =>
            {
                bytesWritten += count;
                temporaryExistedDuringWrite = File.Exists(temporary) && new FileInfo(temporary).Length > 0;
                cancelled.Cancel();
            });
        }));

        Assert.Equal(1, streamsOpened);
        Assert.True(cancelled.IsCancellationRequested);
        Assert.True(bytesWritten > 0);
        Assert.True(temporaryExistedDuringWrite);
        Assert.NotNull(temporaryPath);
        Assert.False(File.Exists(temporaryPath));
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    private static SavedAnalysis State(string text) => new()
    {
        Source = new SourceStamp(new string('A', 64), text.Length, 1),
        PastedText = text
    };

    private static Task SaveWithStream(SavedAnalysis state, string path, CancellationToken token, Func<string, Stream> openStream) =>
        (Task)typeof(AnalysisFiles).GetMethod("SaveAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(SavedAnalysis)).Invoke(null, [state, "state", path, null, token, openStream])!;

    private static FileStream CreateTemporary(string path) =>
        new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);

    private sealed class ObservedWriteStream(FileStream inner, Action<int> afterWrite) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            inner.Write(buffer);
            inner.Flush();
            afterWrite(buffer.Length);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await inner.WriteAsync(buffer, cancellationToken);
            await inner.FlushAsync(cancellationToken);
            afterWrite(buffer.Length);
        }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-analysis-file-limits", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public string File(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
