using System.Windows;

namespace ThreadLogViewer.App;

public static class ExceptionWindow
{
    public static void Show(UserExceptionDecision decision)
    {
        if (!decision.ShowToUser || decision.Message.Length == 0) return;
        var application = Application.Current;
        if (application is null) return;
        void Display()
        {
            try
            {
                Window? owner = application.MainWindow is { IsVisible: true } main ? main : null;
                if (owner is null)
                {
                    foreach (Window window in application.Windows)
                    {
                        if (!window.IsVisible) continue;
                        owner = window;
                        break;
                    }
                }
                if (owner is null) return;
                MessageBox.Show(owner, decision.Message, decision.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
                // A failed dialog must not replace the original failure.
            }
        }
        try
        {
            if (application.Dispatcher.CheckAccess()) Display();
            else if (!application.Dispatcher.HasShutdownStarted) application.Dispatcher.BeginInvoke((Action)Display);
        }
        catch
        {
            // The dispatcher may already be shutting down.
        }
    }
}
