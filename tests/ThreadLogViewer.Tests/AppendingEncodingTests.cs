using System.IO;
using System.Text;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class AppendingEncodingTests
{
    private const string Seed = "[000:00:01] [T1] 합성원문 가나다 똠방각하\n";
    private const string Added = "[000:00:02] [T2] 합성추가 한글\n";

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("cp949")]
    [InlineData("cp949-explicit")]
    public async Task RefreshWhileAppendingKeepsNewKoreanLines(string name)
    {
        var sample = Sample(name);
        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-append.log");
        await using var writer = OpenWriter(path);
        await Write(writer, Frame(sample, Seed));

        var opened = await LogFileReader.ReadAsync(path, sample.Mode);
        AssertLines(opened.Text, Seed);
        Assert.Contains(sample.DescriptionPart, opened.EncodingDescription);
        Assert.DoesNotContain("미완성", opened.EncodingDescription);

        byte[] before = await File.ReadAllBytesAsync(path);
        await Write(writer, sample.Encoding.GetBytes(Added));
        byte[] after = await File.ReadAllBytesAsync(path);
        var refreshed = await LogFileReader.ReadAsync(path, sample.Mode);

        AssertLines(refreshed.Text, Seed, Added);
        Assert.Contains(sample.DescriptionPart, refreshed.EncodingDescription);
        Assert.DoesNotContain("미완성", refreshed.EncodingDescription);
        if (name.StartsWith("cp949", StringComparison.Ordinal))
            Assert.Contains("CP949", refreshed.EncodingDescription, StringComparison.Ordinal);
        else
            Assert.DoesNotContain("CP949", refreshed.EncodingDescription, StringComparison.Ordinal);
        Assert.Equal(after, await File.ReadAllBytesAsync(path));
        Assert.Equal(after, before.Concat(sample.Encoding.GetBytes(Added)).ToArray());
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("cp949")]
    [InlineData("cp949-explicit")]
    public async Task SplitCharacterAtAppendBoundaryIsDroppedThenCompleted(string name)
    {
        var sample = Sample(name);
        byte[] character = sample.Encoding.GetBytes("가");
        Assert.True(character.Length >= 2);
        byte[] partial = character[..^1];
        byte[] rest = character[^1..].Concat(sample.Encoding.GetBytes("나다 이어서\n")).ToArray();
        const string completed = "가나다 이어서\n";

        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-split.log");
        await using var writer = OpenWriter(path);
        await Write(writer, Frame(sample, Seed));
        await Write(writer, partial);

        byte[] during = await File.ReadAllBytesAsync(path);
        var truncated = await LogFileReader.ReadAsync(path, sample.Mode);
        AssertLines(truncated.Text, Seed);
        Assert.Contains("끝의 미완성 문자 제외", truncated.EncodingDescription);
        Assert.Contains(sample.DescriptionPart, truncated.EncodingDescription);
        Assert.Equal(during, await File.ReadAllBytesAsync(path));

        await Write(writer, rest);
        byte[] finished = await File.ReadAllBytesAsync(path);
        var refreshed = await LogFileReader.ReadAsync(path, sample.Mode);
        AssertLines(refreshed.Text, Seed, completed);
        Assert.DoesNotContain("미완성", refreshed.EncodingDescription);
        Assert.Equal(finished, await File.ReadAllBytesAsync(path));
        Assert.Equal(1, Count(refreshed.Text, "가나다 이어서"));
        Assert.Equal(1, Count(refreshed.Text, "합성원문"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf16SurrogateSplitAtAppendBoundaryIsDroppedThenCompleted(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, false, true);
        byte[] bom = bigEndian ? new byte[] { 0xFE, 0xFF } : new byte[] { 0xFF, 0xFE };
        byte[] emoji = encoding.GetBytes("😀");
        Assert.Equal(4, emoji.Length);
        string expected = Seed + "😀\n";

        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-surrogate.log");
        await using var writer = OpenWriter(path);
        await Write(writer, bom.Concat(encoding.GetBytes(Seed)).ToArray());
        await Write(writer, emoji[..2]);

        var truncated = await LogFileReader.ReadAsync(path);
        AssertLines(truncated.Text, Seed);
        Assert.Contains(bigEndian ? "UTF-16 BE" : "UTF-16 LE", truncated.EncodingDescription);
        Assert.Contains("끝의 미완성 문자 제외", truncated.EncodingDescription);

        await Write(writer, emoji[2..].Concat(encoding.GetBytes("\n")).ToArray());
        var refreshed = await LogFileReader.ReadAsync(path);
        Assert.Equal(expected, refreshed.Text);
        AssertLines(refreshed.Text, Seed, "😀\n");
        Assert.DoesNotContain("미완성", refreshed.EncodingDescription);
    }

    [Fact]
    public async Task Utf8BomFirstSnapshotIsOnlyTheBom()
    {
        using var folder = new SyntheticFolder();
        string path = Path.Combine(folder.Path, "synthetic-bom-only.log");
        await using var writer = OpenWriter(path);
        await Write(writer, new byte[] { 0xEF, 0xBB, 0xBF });

        var first = await LogFileReader.ReadAsync(path);
        Assert.Equal("", first.Text);
        Assert.Empty(first.Lines);
        Assert.Equal("UTF-8 (BOM 확인)", first.EncodingDescription);
        Assert.DoesNotContain('\uFEFF', first.Text);
        Assert.DoesNotContain('\uFFFD', first.Text);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, await File.ReadAllBytesAsync(path));

        var utf8 = new UTF8Encoding(false, true);
        await Write(writer, utf8.GetBytes(Seed + Added));
        var refreshed = await LogFileReader.ReadAsync(path);
        AssertLines(refreshed.Text, Seed, Added);
        Assert.Equal("UTF-8 (BOM 확인)", refreshed.EncodingDescription);
        Assert.Equal(1, Count(refreshed.Text, "합성원문"));
        Assert.Equal(1, Count(refreshed.Text, "합성추가"));
    }

    private static void AssertLines(string text, params string[] lines)
    {
        Assert.Equal(string.Concat(lines), text);
        Assert.DoesNotContain('\uFFFD', text);
        Assert.DoesNotContain('\uFEFF', text);
        Assert.DoesNotContain(text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'), line => line.StartsWith('\uFEFF'));
        foreach (string line in lines)
            Assert.Equal(1, Count(text, line.TrimEnd('\n')));
    }

    private static int Count(string text, string value)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static async Task Write(FileStream writer, byte[] bytes)
    {
        await writer.WriteAsync(bytes);
        await writer.FlushAsync();
    }

    private static FileStream OpenWriter(string path) =>
        new(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    private static byte[] Frame(EncodedSample sample, string text) =>
        sample.Bom.Concat(sample.Encoding.GetBytes(text)).ToArray();

    private static EncodedSample Sample(string name)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return name switch
        {
            "utf8" => new(EncodingMode.Auto, [], new UTF8Encoding(false, true), "UTF-8로 해석"),
            "utf8bom" => new(EncodingMode.Auto, new byte[] { 0xEF, 0xBB, 0xBF }, new UTF8Encoding(false, true), "UTF-8 (BOM 확인)"),
            "utf16le" => new(EncodingMode.Auto, new byte[] { 0xFF, 0xFE }, new UnicodeEncoding(false, false, true), "UTF-16 LE"),
            "utf16be" => new(EncodingMode.Auto, new byte[] { 0xFE, 0xFF }, new UnicodeEncoding(true, false, true), "UTF-16 BE"),
            "cp949" => new(EncodingMode.Auto, [], StrictCp949(), "CP949로 해석"),
            "cp949-explicit" => new(EncodingMode.Cp949, [], StrictCp949(), "사용자 지정"),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null)
        };
    }

    private static Encoding StrictCp949() =>
        Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private readonly record struct EncodedSample(EncodingMode Mode, byte[] Bom, Encoding Encoding, string DescriptionPart);

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-append-encoding", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
