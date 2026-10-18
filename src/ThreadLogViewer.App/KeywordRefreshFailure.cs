using System.Text;

namespace ThreadLogViewer.App;

/// <summary>
/// Status transition for a keyword-highlight refresh. A failure replaces "강조 준비 중…"
/// so the highlight label cannot stay on the preparing text. Cancellation is not a failure.
/// </summary>
public static class KeywordRefreshFailure
{
    public const string Preparing = "강조 준비 중…";
    public const string FailedKeyword = "강조 준비 실패";
    public const string FailedOperation = "강조 준비 실패 · 규칙을 확인한 뒤 다시 시도하세요.";
    public const string Memory = "강조 메모리 부족 · 규칙을 줄여 주세요.";

    public readonly record struct Status(string KeywordStatus, string OperationStatus);

    /// <summary>One highlight rule that could not be prepared. Other rules still run.</summary>
    public readonly record struct SkippedRule(int Number, int ColorIndex, string Phrase);

    public static Status Apply(Status current, Exception exception)
    {
        if (exception is OperationCanceledException) return current;
        if (exception is OutOfMemoryException) return new(Memory, Memory);
        return new(FailedKeyword, FailedOperation);
    }

    /// <summary>
    /// A highlight phrase is prepared as a plain string. An unpaired surrogate is not a
    /// searchable character, so that rule is skipped instead of matching inside the log.
    /// </summary>
    public static bool CanPrepare(string? phrase) => phrase is not null && !HasUnpairedSurrogate(phrase);

    public static void EnsureCanPrepare(string phrase)
    {
        if (!CanPrepare(phrase))
            throw new ArgumentException("강조 규칙에 준비할 수 없는 문자가 있습니다.");
    }

    /// <summary>
    /// Status-bar notice in the existing failure style. Each skipped rule is named by its
    /// list number, color, and a readable phrase with unpaired surrogates removed.
    /// </summary>
    public static string SkippedNotice(IReadOnlyList<SkippedRule> skipped, int highlightedPlaces)
    {
        string names = string.Join(", ", skipped.Select(Describe));
        string shown = highlightedPlaces > 0 ? $"표시 {highlightedPlaces:N0}곳 · " : "";
        return $"강조 준비 실패 · {names} · {shown}규칙을 확인한 뒤 다시 시도하세요.";
    }

    private static string Describe(SkippedRule rule)
    {
        string phrase = DisplayPhrase(rule.Phrase);
        string color = HighlightPrompt.ColorName(rule.ColorIndex);
        string identity = color.Length == 0 ? $"{rule.Number}번" : $"{rule.Number}번 {color}";
        return phrase.Length == 0 ? identity : $"{identity} ‘{phrase}’";
    }

    private static string DisplayPhrase(string? phrase)
    {
        if (string.IsNullOrEmpty(phrase)) return "";
        var builder = new StringBuilder(phrase.Length);
        for (int i = 0; i < phrase.Length; i++)
        {
            if (char.IsHighSurrogate(phrase[i]))
            {
                if (i + 1 < phrase.Length && char.IsLowSurrogate(phrase[i + 1]))
                {
                    builder.Append(phrase[i]);
                    builder.Append(phrase[i + 1]);
                    i++;
                }
            }
            else if (!char.IsLowSurrogate(phrase[i]))
                builder.Append(phrase[i]);
        }
        string text = builder.ToString().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        if (text.Length <= 24) return text;
        int end = 24;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end] + "…";
    }

    private static bool HasUnpairedSurrogate(string phrase)
    {
        for (int i = 0; i < phrase.Length; i++)
        {
            if (char.IsHighSurrogate(phrase[i]))
            {
                if (i + 1 >= phrase.Length || !char.IsLowSurrogate(phrase[i + 1])) return true;
                i++;
            }
            else if (char.IsLowSurrogate(phrase[i])) return true;
        }
        return false;
    }
}
