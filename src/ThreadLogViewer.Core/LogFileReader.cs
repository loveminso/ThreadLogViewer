using System.Text;

namespace ThreadLogViewer.Core;

public static class LogFileReader
{
    static LogFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static async Task<LogData> ReadAsync(string path, EncodingMode mode = EncodingMode.Auto,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null)
    {
        // Caller may use Task.Run to keep decoding/parsing off a UI synchronization context.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > int.MaxValue) throw new IOException("2GB 이상의 파일을 지원하지 않습니다.");
        byte[] bytes = new byte[(int)stream.Length];
        int read = 0;
        while (read < bytes.Length)
        {
            int count = await stream.ReadAsync(bytes.AsMemory(read, Math.Min(1024 * 1024, bytes.Length - read)), cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new IOException("읽는 도중 파일 길이가 변경되었습니다. 다시 열어 주세요.");
            read += count;
            progress?.Report(new("파일 읽기", 100.0 * read / bytes.Length));
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new("텍스트 디코딩", 0));
        var decoded = Decode(bytes, mode);
        cancellationToken.ThrowIfCancellationRequested();
        return LogParser.Parse(decoded.Text, Path.GetFullPath(path), decoded.Description, cancellationToken, progress);
    }

    public static (string Text, string Description) Decode(byte[] bytes, EncodingMode mode = EncodingMode.Auto)
    {
        if (mode == EncodingMode.Cp949)
            return (Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes),
                "CP949 (사용자 지정; 자동 판별 아님)");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return (new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3), "UTF-8 (BOM 확인)");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
            throw new InvalidDataException("UTF-32는 지원하지 않습니다.");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return (new UnicodeEncoding(false, true, true).GetString(bytes, 2, bytes.Length - 2), "UTF-16 LE (BOM 확인)");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return (new UnicodeEncoding(true, true, true).GetString(bytes, 2, bytes.Length - 2), "UTF-16 BE (BOM 확인)");
        try { return (new UTF8Encoding(false, true).GetString(bytes), "UTF-8로 해석 (BOM 없음; 추정)"); }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("BOM이 없고 올바른 UTF-8로 읽을 수 없습니다. ‘CP949로 다시 읽기’를 사용하세요.");
        }
    }
}
