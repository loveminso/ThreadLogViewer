using System.Buffers;
using System.Text;

namespace ThreadLogViewer.Core;

public static class LogFileReader
{
    static LogFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Task<LogData> ReadAsync(string path, EncodingMode mode = EncodingMode.Auto,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null)
        => ReadSnapshotAsync(path, mode, cancellationToken, progress, null);

    // Open, F5 refresh, encoding reload, and analysis restore all read the original log through here.
    // The source fingerprint hashes decoded text already in memory and does not reopen the file.
    // The settings store's identity check already opens the original read-only with the same share mode.
    //
    // FileAccess.Read keeps this handle from writing the source. FileShare.ReadWrite | FileShare.Delete
    // lets a logger that still has the file open for append, or that renames or deletes it while rotating,
    // avoid a sharing violation. Length is captured once, immediately after open and before any byte is read.
    // Growth after that capture is not included. A shrink that prevents filling the captured length still
    // fails with "읽는 도중 파일 길이가 변경되었습니다. 다시 열어 주세요."
    // afterLengthCaptured is a synthetic-test seam so a shrink or append can run after that capture
    // without timing. Production passes null, and the seam itself does not write the source.
    internal static async Task<LogData> ReadSnapshotAsync(string path, EncodingMode mode,
        CancellationToken cancellationToken, IProgress<WorkProgress>? progress,
        Func<string, long, CancellationToken, Task>? afterLengthCaptured)
    {
        // Caller may use Task.Run to keep decoding/parsing off a UI synchronization context.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long capturedLength = stream.Length;
        if (capturedLength > int.MaxValue) throw new IOException("2GB 이상의 파일을 지원하지 않습니다.");
        cancellationToken.ThrowIfCancellationRequested();
        if (afterLengthCaptured is not null)
            await afterLengthCaptured(path, capturedLength, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] bytes = new byte[(int)capturedLength];
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
        var decoded = Decode(bytes, mode, cancellationToken, progress);
        // Do not keep the full encoded byte array alive while building the parsed source model.
        bytes = [];
        cancellationToken.ThrowIfCancellationRequested();
        return LogParser.Parse(decoded.Text, Path.GetFullPath(path), decoded.Description, cancellationToken, progress);
    }

    public static (string Text, string Description) Decode(byte[] bytes, EncodingMode mode = EncodingMode.Auto,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mode == EncodingMode.Cp949)
            return (DecodeInBlocks(bytes, 0, Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), cancellationToken, progress),
                "CP949 (사용자 지정; 자동 판별 아님)");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return (DecodeInBlocks(bytes, 3, new UTF8Encoding(false, true), cancellationToken, progress), "UTF-8 (BOM 확인)");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
            throw new InvalidDataException("UTF-32는 지원하지 않습니다.");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return (DecodeInBlocks(bytes, 2, new UnicodeEncoding(false, true, true), cancellationToken, progress), "UTF-16 LE (BOM 확인)");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return (DecodeInBlocks(bytes, 2, new UnicodeEncoding(true, true, true), cancellationToken, progress), "UTF-16 BE (BOM 확인)");
        try { return (DecodeInBlocks(bytes, 0, new UTF8Encoding(false, true), cancellationToken, progress), "UTF-8로 해석 (BOM 없음; 추정)"); }
        catch (DecoderFallbackException)
        {
            // This is a decoding preference, not reliable encoding identification.
            // A recognized BOM is authoritative and its malformed payload never reaches this fallback.
            try
            {
                return (DecodeInBlocks(bytes, 0, Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), cancellationToken, progress),
                    "CP949로 해석 (UTF-8 실패 후 대체; 추정)");
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("BOM이 없고 UTF-8과 CP949 모두로 올바르게 읽을 수 없습니다.", ex);
            }
        }
    }

    private static string DecodeInBlocks(byte[] bytes, int start, Encoding encoding,
        CancellationToken token, IProgress<WorkProgress>? progress)
    {
        const int block = 65536;
        int length = bytes.Length - start, position = 0, characters = 0;
        char[] scratch = ArrayPool<char>.Shared.Rent(block);
        try
        {
            var decoder = encoding.GetDecoder();
            do
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(block, length - position);
                decoder.Convert(bytes.AsSpan(start + position, count), scratch.AsSpan(), position + count == length,
                    out int consumed, out int written, out _);
                position += consumed; characters = checked(characters + written);
                progress?.Report(new("텍스트 디코딩", 45.0 * position / Math.Max(1, length)));
            } while (position < length);
        }
        finally { ArrayPool<char>.Shared.Return(scratch); }
        token.ThrowIfCancellationRequested();
        // Allocate the immutable decoded string once. Stateful decoders retain split multi-byte
        // sequences across blocks, and both passes observe cancellation at every block.
        return string.Create(characters, (bytes, start, length, encoding, token, progress), static (output, state) =>
        {
            var decoder = state.encoding.GetDecoder();
            int position = 0, writtenTotal = 0;
            do
            {
                state.token.ThrowIfCancellationRequested();
                int count = Math.Min(block, state.length - position);
                decoder.Convert(state.bytes.AsSpan(state.start + position, count), output[writtenTotal..],
                    position + count == state.length, out int consumed, out int written, out _);
                position += consumed; writtenTotal += written;
                state.progress?.Report(new("텍스트 디코딩", 50 + 50.0 * position / Math.Max(1, state.length)));
            } while (position < state.length);
            state.token.ThrowIfCancellationRequested();
        });
    }
}
