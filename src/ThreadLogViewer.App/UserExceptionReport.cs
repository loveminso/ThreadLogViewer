namespace ThreadLogViewer.App;

public enum ExceptionSource
{
    Dispatcher,
    UnobservedTask,
    AppDomain,
    AsyncHandler
}

public readonly record struct UserExceptionDecision(
    bool ShowToUser,
    bool MarkDispatcherHandled,
    bool ObserveTask,
    string Title,
    string Message,
    string ExceptionType);

/// <summary>
/// Decides the Korean message and whether the process should keep running.
/// The opened log is never read or written here.
/// </summary>
public static class UserExceptionReport
{
    public const string Title = "예상하지 못한 오류";

    public static UserExceptionDecision Decide(Exception exception, ExceptionSource source)
    {
        if (IsCancellation(exception))
            return new(false, source == ExceptionSource.Dispatcher, true, "", "", "");

        var root = Unwrap(exception);
        bool fatal = IsFatal(root);
        bool keepRunning = source switch
        {
            ExceptionSource.Dispatcher => !fatal,
            ExceptionSource.AppDomain => false,
            _ => true
        };
        string typeName = root.GetType().Name;
        string message = keepRunning ? ContinuingMessage(typeName) : TerminatingMessage(typeName);
        return new(
            true,
            source == ExceptionSource.Dispatcher && !fatal,
            true,
            Title,
            message,
            typeName);
    }

    public static string ContinuingMessage(string exceptionType) =>
        $"예상하지 못한 오류가 발생했습니다 ({exceptionType}). 이 창을 닫으면 앱은 계속 실행됩니다.";

    public static string TerminatingMessage(string exceptionType) =>
        $"예상하지 못한 오류가 발생했습니다 ({exceptionType}). 앱을 계속 실행할 수 없습니다.";

    public static bool IsCancellation(Exception exception)
    {
        exception = Unwrap(exception);
        if (exception is OperationCanceledException) return true;
        return exception is AggregateException aggregate
            && aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(item => item is OperationCanceledException);
    }

    public static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or AccessViolationException or StackOverflowException;

    public static Exception Unwrap(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            var flat = aggregate.Flatten();
            if (flat.InnerExceptions.Count == 1) return Unwrap(flat.InnerExceptions[0]);
        }
        return exception;
    }
}
