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

    public static Status Apply(Status current, Exception exception)
    {
        if (exception is OperationCanceledException) return current;
        if (exception is OutOfMemoryException) return new(Memory, Memory);
        return new(FailedKeyword, FailedOperation);
    }
}
