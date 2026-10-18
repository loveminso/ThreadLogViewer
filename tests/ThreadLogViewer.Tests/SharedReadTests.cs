using System.IO;
using System.Text;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

// A writer that keeps the log open with FileShare.ReadWrite must not block a read-only open.
// The read captures the length at open: later growth is left out until the next open, and a
// shrink that cannot fill that length raises the Korean error. F5, encoding reload, and
// analysis restore all call LogFileReader.ReadAsync, so they follow the same rules.
public sealed class SharedReadTests
{
    private const string LengthChanged = "읽는 도중 파일 길이가 변경되었습니다. 다시 열어 주세요.";
    private const string Prefix = "[000:00:01] [T1] synthetic shared read\n";
    private const string Grown = "[000:00:02] [T2] synthetic grew after open\n";

    [Fact]
    public async Task ReadAsyncSucceedsWhileAnotherStreamWritesAndLeavesBytesAndLastWriteTimeUnchanged()
    {
        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-shared.log");
        byte[] original = Encoding.UTF8.GetBytes(Prefix);
        await File.WriteAllBytesAsync(path, original);
        File.SetLastWriteTimeUtc(path, new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        using var writer = OpenWriter(path);
        byte[] beforeBytes = ReadShared(path);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(path);

        var data = await LogFileReader.ReadAsync(path);

        Assert.Equal(Prefix, data.Text);
        Assert.Equal(beforeBytes, ReadShared(path));
        Assert.Equal(original, ReadShared(path));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task ShrinkAfterLengthCaptureThrowsKoreanError()
    {
        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-shrink.log");
        byte[] original = Encoding.UTF8.GetBytes(Prefix);
        await File.WriteAllBytesAsync(path, original);
        using var writer = OpenWriter(path);

        var error = await Assert.ThrowsAsync<IOException>(() => LogFileReader.ReadSnapshotAsync(path, EncodingMode.Auto, default, null,
            (_, captured, _) =>
            {
                Assert.Equal(original.Length, captured);
                writer.SetLength(10);
                writer.Flush(true);
                return Task.CompletedTask;
            }));

        Assert.Equal(LengthChanged, error.Message);
        byte[] onDisk = ReadShared(path);
        Assert.Equal(10, onDisk.Length);
        Assert.Equal(original[..10], onDisk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GrowthAfterLengthCaptureIsExcludedUntilTheNextOpen(bool startEmpty)
    {
        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-grow.log");
        string prefix = startEmpty ? "" : Prefix;
        byte[] original = Encoding.UTF8.GetBytes(prefix);
        byte[] extra = Encoding.UTF8.GetBytes(Grown);
        await File.WriteAllBytesAsync(path, original);
        using var writer = OpenWriter(path);

        var data = await LogFileReader.ReadSnapshotAsync(path, EncodingMode.Auto, default, null,
            async (_, captured, token) =>
            {
                Assert.Equal(original.Length, captured);
                writer.Seek(0, SeekOrigin.End);
                await writer.WriteAsync(extra.AsMemory(), token);
                writer.Flush(true);
            });

        Assert.Equal(prefix, data.Text);
        Assert.DoesNotContain("grew after open", data.Text);
        Assert.Equal(original.Concat(extra).ToArray(), ReadShared(path));

        var reread = await LogFileReader.ReadAsync(path);
        Assert.Equal(prefix + Grown, reread.Text);
        Assert.Contains("grew after open", reread.Text);
    }

    private static FileStream OpenWriter(string path) =>
        new(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.GetFullPath(System.IO.Path.Combine("TestResults", Guid.NewGuid().ToString("N")));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
