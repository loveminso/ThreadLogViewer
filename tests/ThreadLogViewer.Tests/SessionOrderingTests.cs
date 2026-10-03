using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class SessionOrderingTests
{
    [Fact]
    public Task ReopeningTheActiveFileInvalidatesAnOlderDelayedLoadAndKeepsItsState() => InSta(async () =>
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "synthetic-session-ordering", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var window = new MainWindow(folder, false);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? pending = null;
        try
        {
            const string firstText = "[000:00:01] [T1] synthetic A header\r\nsynthetic A body";
            const string secondText = "[000:00:02] [T7] synthetic delayed B header\nsynthetic delayed B body";
            string first = Path.Combine(folder, "synthetic-A.log"), second = Path.Combine(folder, "synthetic-B.log");
            await File.WriteAllTextAsync(first, firstText, new UTF8Encoding(false, true));
            await File.WriteAllTextAsync(second, secondText, new UTF8Encoding(false, true));
            Assert.True(await (Task<bool>)Invoke(window, "OpenAsync", first, EncodingMode.Auto)!);
            var tabs = Control<ListBox>(window, "SessionTabs");
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.Caret.Line = 2;
            editor.TextArea.Caret.Column = 5;
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Invoke(window, "SetTimePoint", true);
            var source = Field<LogData>(window, "data");
            var view = Field<LogProjection>(window, "projection");
            var time = Field<TimeAnchor>(window, "timeA");
            object tab = tabs.SelectedItem;
            var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> loadSecond = async (token, progress) =>
            {
                started.SetResult(token);
                await release.Task;
                // Model a reader that has already passed its cancellation check and finishes late.
                var result = await LogFileReader.ReadAsync(second);
                progress.Report(new("synthetic late B progress", 100));
                return result;
            };
            pending = (Task<bool>)Invoke(window, "LoadAsync", loadSecond, "synthetic delayed B load", "synthetic delayed B failure")!;
            var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Field<bool>(window, "busy"));

            Assert.True(await (Task<bool>)Invoke(window, "OpenAsync", Path.Combine(folder, ".", "synthetic-A.log"), EncodingMode.Auto)!);
            Assert.True(token.IsCancellationRequested);
            Assert.False(Field<bool>(window, "busy"));
            release.SetResult();
            Assert.False(await pending);
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            Assert.Single(tabs.Items.Cast<object>());
            Assert.Same(tab, tabs.SelectedItem);
            Assert.Same(source, Field<LogData>(window, "data"));
            Assert.Same(view, Field<LogProjection>(window, "projection"));
            Assert.Same(time, Field<TimeAnchor>(window, "timeA"));
            Assert.Equal(1, Assert.Single(Field<BookmarkState>(window, "bookmarks").Items).SourceLineIndex);
            Assert.Equal(first, Field<string>(window, "requestedPath"));
            Assert.Equal(firstText, editor.Text);
            Assert.Equal(2, editor.TextArea.Caret.Line);
            Assert.Equal(5, editor.TextArea.Caret.Column);
            Assert.DoesNotContain("synthetic late B", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.Equal(firstText, await File.ReadAllTextAsync(first));
            Assert.Equal(secondText, await File.ReadAllTextAsync(second));
        }
        finally
        {
            release.TrySetResult();
            if (pending is not null) await pending;
            window.Close();
            Directory.Delete(folder, true);
        }
    });

    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments);
    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;
}
