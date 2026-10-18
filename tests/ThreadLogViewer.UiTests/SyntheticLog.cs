using System.Text;

namespace ThreadLogViewer.UiTests;

internal enum LogEncodingKind
{
    Utf8,
    Utf8Bom,
    Cp949
}

internal static class SyntheticLog
{
    public const string OriginalKorean = "합성원문가나다";
    public const string Distinctive = "똠방각하";
    public const string AppendMarker = "합성추가";
    public const string KeepToken = "alpha-keep";
    public const string HideToken = "beta-hide";
    public const string KeepKorean = "합성필터";
    public const string HideKorean = "합성숨김";
    public const string SearchToken = "marker-zeta";

    public static void RegisterEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding EncodingOf(LogEncodingKind kind) 
    {
        RegisterEncodings();
        return kind == LogEncodingKind.Cp949
            ? Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    }

    public static string Line(int second, int thread, string message) =>
        $"[000:00:{second:00}.000] [T {thread}] {message} (synthetic.cpp:{second})\n";

    public static void Write(string path, LogEncodingKind kind, string text)
    {
        byte[] body = EncodingOf(kind).GetBytes(text);
        if (kind == LogEncodingKind.Utf8Bom)
            body = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray();
        File.WriteAllBytes(path, body);
    }

    public static string RefreshSeed() =>
        Line(1, 1, $"{OriginalKorean} {Distinctive}");

    public static string FilterLog() =>
        Line(1, 1, $"{KeepToken} {KeepKorean}") +
        Line(2, 2, $"{HideToken} {HideKorean}");

    public static string SearchLog() =>
        Line(1, 1, $"{SearchToken} one") +
        Line(2, 1, $"{SearchToken} two") +
        Line(3, 1, $"{SearchToken} three");

    public static string MixedLog() =>
        Line(1, 1, $"{KeepToken} {KeepKorean} {SearchToken}") +
        Line(2, 2, $"{HideToken} {HideKorean}") +
        Line(3, 1, $"{OriginalKorean} {Distinctive}");
}
