using System.Windows;

namespace ThreadLogViewer.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            var decision = UserExceptionReport.Decide(e.Exception, ExceptionSource.Dispatcher);
            if (decision.ShowToUser)
            {
                LocalExceptionLog.TryWrite(decision.ExceptionType, "dispatcher");
                ExceptionWindow.Show(decision);
            }
            if (decision.MarkDispatcherHandled) e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            var decision = UserExceptionReport.Decide(e.Exception, ExceptionSource.UnobservedTask);
            if (decision.ObserveTask) e.SetObserved();
            if (!decision.ShowToUser) return;
            LocalExceptionLog.TryWrite(decision.ExceptionType, "unobserved-task");
            ExceptionWindow.Show(decision);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception exception) return;
            var decision = UserExceptionReport.Decide(exception, ExceptionSource.AppDomain);
            if (!decision.ShowToUser) return;
            LocalExceptionLog.TryWrite(decision.ExceptionType, "appdomain");
            ExceptionWindow.Show(decision);
        };
    }
}
