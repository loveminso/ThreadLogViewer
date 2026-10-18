namespace ThreadLogViewer.App;

/// <summary>
/// The only entry point for UI events that start asynchronous work.
/// Callers stay synchronous so an exception cannot leave an event handler.
/// </summary>
public static class AsyncEvent
{
    public static void Run(Func<Task> action) => _ = Protect(action, Report);

    public static Task Run(Func<Task> action, Action<UserExceptionDecision> present) => Protect(action, present);

    private static async Task Protect(Func<Task> action, Action<UserExceptionDecision> present)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            var decision = UserExceptionReport.Decide(exception, ExceptionSource.AsyncHandler);
            if (!decision.ShowToUser) return;
            try { present(decision); }
            catch { /* Reporting must not escape the handler. */ }
        }
    }

    private static void Report(UserExceptionDecision decision)
    {
        LocalExceptionLog.TryWrite(decision.ExceptionType, "async-handler");
        ExceptionWindow.Show(decision);
    }
}
