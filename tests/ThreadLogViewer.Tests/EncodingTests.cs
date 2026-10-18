using System.IO;
using System.Text;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class EncodingTests
{
    [Fact]
    public void BomlessUtf8WinsEvenWhenTheSameBytesAreAlsoValidCp949()
    {
        byte[] bytes = [0xC3, 0xA9]; // UTF-8 é is also a valid, different CP949 character.
        var automatic = LogFileReader.Decode(bytes);
        var explicitCp949 = LogFileReader.Decode(bytes, EncodingMode.Cp949);
        Assert.Equal("é", automatic.Text);
        Assert.NotEqual(automatic.Text, explicitCp949.Text);
        Assert.Contains("UTF-8", automatic.Description);
        Assert.Contains("추정", automatic.Description);
        Assert.DoesNotContain("대체", automatic.Description);
        Assert.Contains("사용자 지정", explicitCp949.Description);
    }

    [Theory]
    [InlineData("[000:00:01] [T7] 합성 읽기 완료\r\n본문\n\r\n")]
    [InlineData("한글 확장: 똠 쀍")]
    public void AutomaticCp949PreservesKoreanAndOriginalLineEndings(string text)
    {
        var cp949 = StrictCp949();
        byte[] bytes = cp949.GetBytes(text);
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(bytes));
        var decoded = LogFileReader.Decode(bytes);
        Assert.Equal(text, decoded.Text);
        Assert.Equal("CP949로 해석 (UTF-8 실패 후 대체; 추정)", decoded.Description);
        Assert.DoesNotContain('\uFFFD', decoded.Text);
        Assert.Equal(bytes, cp949.GetBytes(decoded.Text));
    }

    [Theory]
    [InlineData(new byte[] { 0x81 })]
    [InlineData(new byte[] { 0xC0, 0x80 })]
    public void BothInvalidEncodingsFailInsteadOfInsertingReplacementCharacters(byte[] bytes)
    {
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(bytes));
        Assert.Throws<DecoderFallbackException>(() => StrictCp949().GetString(bytes));
        var failure = Assert.Throws<InvalidDataException>(() => LogFileReader.Decode(bytes));
        Assert.Contains("UTF-8과 CP949 모두", failure.Message);
        Assert.IsType<DecoderFallbackException>(failure.InnerException);
        Assert.Throws<DecoderFallbackException>(() => LogFileReader.Decode(bytes, EncodingMode.Cp949));
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA9, 0x81 })]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x80 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00 })]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0xD8 })]
    [InlineData(new byte[] { 0xFE, 0xFF, 0xD8, 0x00 })]
    public void RecognizedBomWithMalformedPayloadNeverFallsBackToCp949(byte[] bytes) =>
        Assert.Throws<DecoderFallbackException>(() => LogFileReader.Decode(bytes));

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41 })]
    public void Utf32BomRemainsUnsupportedBeforeUtf16Handling(byte[] bytes) =>
        Assert.Contains("UTF-32", Assert.Throws<InvalidDataException>(() => LogFileReader.Decode(bytes)).Message);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TruncatedUtf8FileDropsOnlyTheUnfinishedKoreanCharacter(int missingBytes)
    {
        using var folder = new SyntheticEncodingFolder();
        string path = Path.Combine(folder.Path, "synthetic-truncated-utf8.log");
        const string complete = "[000:00:01] [T1] 완성\n[000:00:02] [T2] 잘";
        byte[] full = new UTF8Encoding(false).GetBytes(complete);
        Assert.Equal(new byte[] { 0xEC, 0x9E, 0x98 }, full[^3..]);
        byte[] truncated = full[..^missingBytes];
        await File.WriteAllBytesAsync(path, truncated);

        var data = await LogFileReader.ReadAsync(path);

        Assert.Equal(complete[..^1], data.Text);
        Assert.Contains("[000:00:02] [T2] ", data.Text);
        Assert.Contains("완성\n", data.Text);
        Assert.DoesNotContain("잘", data.Text);
        Assert.DoesNotContain("\uFFFD", data.Text);
        Assert.Equal("UTF-8로 해석 (BOM 없음; 추정) (끝의 미완성 문자 제외)", data.EncodingDescription);
        Assert.Equal(truncated, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task CompleteUtf8FileKeepsTheEstimatedDescription()
    {
        using var folder = new SyntheticEncodingFolder();
        string path = Path.Combine(folder.Path, "synthetic-utf8.log");
        const string text = "[000:00:01] [T1] 완성\n[000:00:02] [T2] 잘\n";
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        await File.WriteAllBytesAsync(path, bytes);

        var data = await LogFileReader.ReadAsync(path);

        Assert.Equal(text, data.Text);
        Assert.Equal("UTF-8로 해석 (BOM 없음; 추정)", data.EncodingDescription);
        Assert.DoesNotContain("미완성", data.EncodingDescription);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Cp949FileStillFallsBackWhenUtf8IsInvalid()
    {
        using var folder = new SyntheticEncodingFolder();
        string path = Path.Combine(folder.Path, "synthetic-cp949-fallback.log");
        const string text = "[000:00:01] [T1] 한글 확장: 똠 쀍\n";
        byte[] bytes = StrictCp949().GetBytes(text);
        Assert.Throws<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(bytes));
        await File.WriteAllBytesAsync(path, bytes);

        var data = await LogFileReader.ReadAsync(path);

        Assert.Equal(text, data.Text);
        Assert.Equal("CP949로 해석 (UTF-8 실패 후 대체; 추정)", data.EncodingDescription);
        Assert.DoesNotContain("미완성", data.EncodingDescription);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void ThreeByteIncompleteScalarAtEofIsExcludedWithoutCuttingTheLine()
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes("line\nmore ").Concat(new byte[] { 0xF0, 0x9F, 0x99 }).ToArray();
        var decoded = LogFileReader.Decode(bytes);
        Assert.Equal("line\nmore ", decoded.Text);
        Assert.Equal("UTF-8로 해석 (BOM 없음; 추정) (끝의 미완성 문자 제외)", decoded.Description);
    }

    [Fact]
    public void IncompleteTailAfterABlockBoundaryIsStillExcluded()
    {
        byte[] bytes = new byte[65536 + 2];
        Array.Fill(bytes, (byte)'x');
        bytes[^2] = 0xEC;
        bytes[^1] = 0x9E;
        var decoded = LogFileReader.Decode(bytes);
        Assert.Equal(new string('x', 65536), decoded.Text);
        Assert.Equal("UTF-8로 해석 (BOM 없음; 추정) (끝의 미완성 문자 제외)", decoded.Description);
    }

    [Theory]
    [InlineData(new byte[] { 0x41, 0xFF })]
    [InlineData(new byte[] { 0x41, 0x80 })]
    [InlineData(new byte[] { 0x41, 0xE0, 0x80 })]
    [InlineData(new byte[] { 0x41, 0xED, 0xA0 })]
    [InlineData(new byte[] { 0x41, 0xC0, 0x80, 0xEA })]
    public void OtherUtf8FailuresDoNotDropATail(byte[] bytes)
    {
        var failure = Assert.Throws<InvalidDataException>(() => LogFileReader.Decode(bytes));
        Assert.Contains("UTF-8과 CP949", failure.Message);
    }

    [Fact]
    public async Task AutomaticCp949ReadMapsOriginalLinesAndNeverChangesSourceBytes()
    {
        using var folder = new SyntheticEncodingFolder();
        string path = Path.Combine(folder.Path, "synthetic-cp949.log");
        const string original = "[000:00:01] [T1] 합성 시작\r\n본문 덤프\r\n[000:00:02] [T7] 합성 끝\n";
        byte[] bytes = StrictCp949().GetBytes(original);
        await File.WriteAllBytesAsync(path, bytes);
        var data = await LogFileReader.ReadAsync(path);
        Assert.Equal(original, data.Text);
        Assert.Equal(Path.GetFullPath(path), data.SourcePath);
        Assert.Equal(new[] { 1, 2, 3 }, data.Lines.Select(x => x.OriginalLineNumber));
        Assert.Equal(1, data.Lines[1].ThreadId);
        Assert.Equal("\r\n", data.Lines[1].LineEnding.ToString());
        Assert.Contains("대체; 추정", data.EncodingDescription);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData(new byte[] { 0x81 }, false)]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0x80 }, true)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, false)]
    public async Task FailedAutomaticReadsLeaveSourceBytesUnchanged(byte[] bytes, bool invalidUtf8Bom)
    {
        using var folder = new SyntheticEncodingFolder();
        string path = Path.Combine(folder.Path, "synthetic-invalid.log");
        await File.WriteAllBytesAsync(path, bytes);
        if (invalidUtf8Bom)
            await Assert.ThrowsAsync<DecoderFallbackException>(() => LogFileReader.ReadAsync(path));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => LogFileReader.ReadAsync(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    private static Encoding StrictCp949()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private sealed class SyntheticEncodingFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-encoding-tests", Guid.NewGuid().ToString("N"));
        public SyntheticEncodingFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
}
