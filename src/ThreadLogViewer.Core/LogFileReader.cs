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
        // F5 reads the whole snapshot again. The failure while a logger is still writing is not a
        // stale decoder and not the BOM being added into the next offset. Each call builds a new
        // decoder, and a BOM is skipped once as `start`. `DecodeInBlocks` then counts bytes from
        // that start. The snapshot length is taken before the read, so the last character can be
        // cut in half. A strict flush used to throw DecoderFallbackException there. UTF-8 without
        // a BOM already dropped that unfinished tail; a recognized BOM returned before that drop,
        // and CP949 / UTF-16 had no drop at all. Drop only an unfinished tail. A malformed byte
        // that is not an unfinished character still fails, and a BOM payload never falls back to CP949.
        if (mode == EncodingMode.Cp949)
            return DecodeAuthoritative(bytes, 0, StrictCp949(), SnapshotTail.Cp949, allowEmptyRemainder: false,
                "CP949 (사용자 지정; 자동 판별 아님)", cancellationToken, progress);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            return DecodeAuthoritative(bytes, 3, StrictUtf8(), SnapshotTail.Utf8, allowEmptyRemainder: true,
                "UTF-8 (BOM 확인)", cancellationToken, progress);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0, 0 }) || bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xFE, 0xFF }))
            throw new InvalidDataException("UTF-32는 지원하지 않습니다.");
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            return DecodeAuthoritative(bytes, 2, new UnicodeEncoding(false, true, true), SnapshotTail.Utf16Le, allowEmptyRemainder: true,
                "UTF-16 LE (BOM 확인)", cancellationToken, progress);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            return DecodeAuthoritative(bytes, 2, new UnicodeEncoding(true, true, true), SnapshotTail.Utf16Be, allowEmptyRemainder: true,
                "UTF-16 BE (BOM 확인)", cancellationToken, progress);
        const string utf8Estimated = "UTF-8로 해석 (BOM 없음; 추정)";
        try
        {
            return DecodeAuthoritative(bytes, 0, StrictUtf8(), SnapshotTail.Utf8, allowEmptyRemainder: true,
                utf8Estimated, cancellationToken, progress);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return DecodeAuthoritative(bytes, 0, StrictCp949(), SnapshotTail.Cp949, allowEmptyRemainder: false,
                    "CP949로 해석 (UTF-8 실패 후 대체; 추정)", cancellationToken, progress);
            }
            catch (DecoderFallbackException ex)
            {
                throw new InvalidDataException("BOM이 없고 UTF-8과 CP949 모두로 올바르게 읽을 수 없습니다.", ex);
            }
        }
    }

    private enum SnapshotTail { Utf8, Utf16Le, Utf16Be, Cp949 }

    private const string UnfinishedTailSuffix = " (끝의 미완성 문자 제외)";

    private static Encoding StrictUtf8() => new UTF8Encoding(false, true);

    private static Encoding StrictCp949() =>
        Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    // `start` is the first payload byte (after a BOM). `length` passed to DecodeInBlocks is a count
    // from `start`, so the BOM width is not subtracted again when the unfinished tail is removed.
    private static (string Text, string Description) DecodeAuthoritative(byte[] bytes, int start, Encoding encoding,
        SnapshotTail tailKind, bool allowEmptyRemainder, string description, CancellationToken token, IProgress<WorkProgress>? progress)
    {
        int payload = bytes.Length - start;
        try
        {
            return (DecodeInBlocks(bytes, start, encoding, token, progress, payload), description);
        }
        catch (DecoderFallbackException)
        {
            int tail = UnfinishedTailLength(bytes.AsSpan(start, payload), tailKind);
            if (tail > 0 && tail <= payload && (allowEmptyRemainder || tail < payload))
            {
                try
                {
                    return (DecodeInBlocks(bytes, start, encoding, token, progress, payload - tail), description + UnfinishedTailSuffix);
                }
                catch (DecoderFallbackException)
                {
                    // The unfinished tail was not the only strict failure.
                }
            }
            throw;
        }
    }

    private static int UnfinishedTailLength(ReadOnlySpan<byte> payload, SnapshotTail kind) => kind switch
    {
        SnapshotTail.Utf8 => IncompleteUtf8TailLength(payload),
        SnapshotTail.Utf16Le => IncompleteUtf16TailLength(payload, bigEndian: false),
        SnapshotTail.Utf16Be => IncompleteUtf16TailLength(payload, bigEndian: true),
        SnapshotTail.Cp949 => IncompleteCp949TailLength(payload),
        _ => 0
    };

    // A trailing odd byte is an unfinished code unit. A trailing high surrogate is an unfinished
    // pair. A lone low surrogate is malformed and stays 0 so the strict decoder still rejects it.
    private static int IncompleteUtf16TailLength(ReadOnlySpan<byte> payload, bool bigEndian)
    {
        if (payload.Length == 0) return 0;
        int end = payload.Length;
        int tail = 0;
        if ((end & 1) == 1)
        {
            tail = 1;
            end--;
        }
        if (end >= 2 && IsHighSurrogate(payload, end - 2, bigEndian))
            tail += 2;
        return tail;
    }

    private static bool IsHighSurrogate(ReadOnlySpan<byte> payload, int index, bool bigEndian)
    {
        int unit = bigEndian
            ? (payload[index] << 8) | payload[index + 1]
            : payload[index] | (payload[index + 1] << 8);
        return unit is >= 0xD800 and <= 0xDBFF;
    }

    // CP949 lead bytes are 0x81–0xFE. 0x80 and 0xFF are complete single-byte characters.
    // A lead without its trail is unfinished. The caller still decodes the prefix strictly,
    // so dropping this byte cannot hide an earlier invalid pair.
    private static int IncompleteCp949TailLength(ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0) return 0;
        byte last = payload[^1];
        return last is >= 0x81 and <= 0xFE ? 1 : 0;
    }

    // 0 when the bytes are well-formed or the first problem is not a 1–3 byte incomplete scalar at EOF.
    private static int IncompleteUtf8TailLength(ReadOnlySpan<byte> bytes)
    {
        int index = 0;
        while (index < bytes.Length)
        {
            byte lead = bytes[index];
            if (lead <= 0x7F) { index++; continue; }
            int expected = Utf8SequenceLength(lead);
            if (expected == 0) return 0;
            int available = bytes.Length - index;
            if (available < expected)
                return IsAcceptableIncompletePrefix(bytes.Slice(index, available)) ? available : 0;
            if (!IsWellFormedUtf8Sequence(bytes.Slice(index, expected))) return 0;
            index += expected;
        }
        return 0;
    }

    private static int Utf8SequenceLength(byte lead) => lead switch
    {
        >= 0xC2 and <= 0xDF => 2,
        >= 0xE0 and <= 0xEF => 3,
        >= 0xF0 and <= 0xF4 => 4,
        _ => 0
    };

    private static bool IsContinuation(byte value) => (value & 0xC0) == 0x80;

    private static bool SecondByteInRange(byte lead, byte second) => IsContinuation(second) && lead switch
    {
        0xE0 => second >= 0xA0,
        0xED => second <= 0x9F,
        0xF0 => second >= 0x90,
        0xF4 => second <= 0x8F,
        _ => true
    };

    private static bool IsWellFormedUtf8Sequence(ReadOnlySpan<byte> sequence)
    {
        if (sequence.Length != Utf8SequenceLength(sequence[0])) return false;
        if (sequence.Length > 1 && !SecondByteInRange(sequence[0], sequence[1])) return false;
        for (int i = 2; i < sequence.Length; i++)
            if (!IsContinuation(sequence[i])) return false;
        return true;
    }

    private static bool IsAcceptableIncompletePrefix(ReadOnlySpan<byte> tail)
    {
        if (tail.Length is < 1 or > 3) return false;
        int expected = Utf8SequenceLength(tail[0]);
        if (expected <= tail.Length) return false;
        for (int i = 1; i < tail.Length; i++)
            if (!IsContinuation(tail[i])) return false;
        return tail.Length == 1 || SecondByteInRange(tail[0], tail[1]);
    }

    private static string DecodeInBlocks(byte[] bytes, int start, Encoding encoding,
        CancellationToken token, IProgress<WorkProgress>? progress, int length = -1)
    {
        const int block = 65536;
        if (length < 0) length = bytes.Length - start;
        int position = 0, characters = 0;
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
        // Allocate the immutable decoded string once. Each pass calls GetDecoder, so an earlier
        // snapshot cannot leave a partial character behind. Inside one pass the decoder keeps a
        // character split across the 64 KiB block boundary. Both passes observe cancellation at every block.
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
