using System.Runtime.ExceptionServices;

namespace ThreadLogViewer.UiTests;

internal static class UiSta
{
    public static void Run(Action action, TimeSpan timeout)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        })
        {
            IsBackground = true,
            Name = "ThreadLogViewer UI STA"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(timeout))
        {
            var timedOut = new TimeoutException($"UI test did not finish within {timeout.TotalSeconds:0}s.");
            OleFileDrop.ReleasePointer();
            UiSession.CaptureActive(timedOut);
            UiSession.AbandonActive();
            throw timedOut;
        }
        if (error is not null)
            ExceptionDispatchInfo.Capture(error).Throw();
    }
}
