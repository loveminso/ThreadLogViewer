using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class ClipboardRegressionTests
{
    [Fact]
    public void InvalidClipboardDataUsesAnExceptionOutsideTheIOExceptionHierarchy()
    {
        // The original GUI only caught IOException, which does not catch this validation type.
        Assert.False(typeof(IOException).IsAssignableFrom(typeof(InvalidDataException)));
    }

    [Fact]
    public void ClipboardProviderFailuresBecomeSafeReadErrors()
    {
        foreach (var failure in ProviderFailures())
        {
            var provider = new SyntheticDataObject { Failure = failure };
            var error = Assert.Throws<IOException>(() => LogTransfer.ReadClipboardFiles(provider));
            Assert.Same(failure, error.InnerException);
            Assert.DoesNotContain("synthetic private payload", error.Message);
            var deferredProvider = new SyntheticDataObject { Failure = failure, FailOnlyOnGetData = true };
            deferredProvider.Payloads[DataFormats.UnicodeText] = "synthetic deferred payload";
            Assert.Same(failure, Assert.Throws<IOException>(() => LogTransfer.ReadClipboardFiles(deferredProvider)).InnerException);
        }
    }

    [Fact]
    public void FileSnapshotSupportsMultipleFilesWithoutConversionOrProviderArrayMutation()
    {
        string[] original = [@"C:\synthetic\first.LOG", @"C:\synthetic\두번째.txt"];
        var provider = new SyntheticDataObject();
        provider.Payloads[DataFormats.FileDrop] = original;
        provider.Payloads[DataFormats.UnicodeText] = "A file selection must not turn into log text";
        var input = LogTransfer.ReadClipboardFiles(provider);
        original[0] = @"C:\synthetic\changed.log";
        Assert.Equal(@"C:\synthetic\first.LOG", input.FilePath);
        Assert.Equal(new[] { @"C:\synthetic\first.LOG", @"C:\synthetic\두번째.txt" }, input.FilePaths);
        Assert.Null(input.Text);
        Assert.False(provider.AutoConversionRequested);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)input.FilePaths)[0] = "changed.log");
        Assert.Throws<InvalidDataException>(() => LogTransfer.ReadClipboard(provider));
    }

    [Fact]
    public void UnsupportedOrMalformedFilePayloadNeverFallsBackToFileNamesAsText()
    {
        foreach (var payload in new object[] { new[] { "first.log", "program.exe" }, Array.Empty<string>(), new[] { "bad\0name.log" }, "first.log" })
        {
            var provider = new SyntheticDataObject();
            provider.Payloads[DataFormats.FileDrop] = payload;
            provider.Payloads[DataFormats.UnicodeText] = "fallback must stay unread";
            Assert.Throws<InvalidDataException>(() => LogTransfer.ReadClipboardFiles(provider));
        }
    }

    [Fact]
    public void UnicodeSnapshotPreservesKoreanAndOriginalLineEndings()
    {
        const string text = "[000:00:01] [T1] 합성 기록\r\n\r\n본문\n";
        var provider = new SyntheticDataObject();
        provider.Payloads[DataFormats.UnicodeText] = text;
        var input = LogTransfer.ReadClipboardFiles(provider);
        Assert.Equal(text, input.Text);
        Assert.Empty(input.FilePaths);
        Assert.Null(input.FilePath);
        Assert.False(provider.AutoConversionRequested);
    }

    [Fact]
    public Task FailedPasteProviderRetainsDocumentAndSessionStateWithoutShowingWindow() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            await Paste(window, () => TextProvider("[000:00:01] [T1] initial\r\nbody\r\n"));
            var initial = Field<LogProjection>(window, "projection");
            var editor = Control<TextEditor>(window, "Editor");
            editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(2).Offset;
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            foreach (var failure in ProviderFailures())
            {
                bool accepted = await Paste(window, () => throw failure);
                Assert.False(accepted);
                Assert.Same(initial, Field<LogProjection>(window, "projection"));
                Assert.Equal(initial.Text, editor.Text);
                Assert.Single(Field<BookmarkState>(window, "bookmarks").Items);
                string status = Control<TextBlock>(window, "OperationStatus").Text;
                Assert.Contains("붙여넣기 실패", status);
                Assert.DoesNotContain("synthetic private payload", status);
            }
            var deferred = new SyntheticDataObject { Failure = new InvalidOperationException("synthetic deferred failure"), FailOnlyOnGetData = true };
            deferred.Payloads[DataFormats.UnicodeText] = "synthetic text";
            Assert.False(await Paste(window, () => deferred));
            Assert.Same(initial, Field<LogProjection>(window, "projection"));
            Assert.Single(Field<BookmarkState>(window, "bookmarks").Items);
            Assert.False(await Paste(window, () => throw new OutOfMemoryException("synthetic memory failure")));
            Assert.Same(initial, Field<LogProjection>(window, "projection"));
            Assert.Contains("메모리", Control<TextBlock>(window, "OperationStatus").Text);
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task EmptyUnsupportedAndRejectedFileClipboardDoesNotEscapePasteOrReplaceDocument() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            Assert.True(await Paste(window, () => TextProvider("[000:00:01] [T1] keep this synthetic log\n")));
            var original = Field<LogProjection>(window, "projection");
            var unsupported = new SyntheticDataObject();
            unsupported.Payloads[DataFormats.Bitmap] = new object();
            var rejectedFile = new SyntheticDataObject();
            rejectedFile.Payloads[DataFormats.FileDrop] = new[] { "synthetic-program.exe" };
            rejectedFile.Payloads[DataFormats.UnicodeText] = "fallback text must not replace the log";
            foreach (IDataObject? input in new IDataObject?[] { null, new SyntheticDataObject(), TextProvider(""), unsupported, rejectedFile })
            {
                Assert.False(await Paste(window, () => input));
                Assert.Same(original, Field<LogProjection>(window, "projection"));
                Assert.Equal(original.Text, Control<TextEditor>(window, "Editor").Text);
                Assert.Contains("붙여넣기 실패", Control<TextBlock>(window, "OperationStatus").Text);
            }
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task PasteLongThenShortTextWithOldSelectionAndTimeStateKeepsIndexesValid() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        var window = new MainWindow(folder.Path, false);
        try
        {
            string longer = string.Join("\r\n", Enumerable.Range(0, 100).Select(i => $"[000:00:{i % 60:00}] [T1] synthetic {i}"));
            Assert.True(await Paste(window, () => TextProvider(longer)));
            var editor = Control<TextEditor>(window, "Editor");
            editor.Select(editor.Document.GetLineByNumber(100).Offset, 4);
            Invoke(window, "BookmarkToggle_Click", window, new RoutedEventArgs());
            Invoke(window, "TimeA_Click", window, new RoutedEventArgs());
            Assert.NotNull(Field<TimeAnchor?>(window, "timeA"));
            Assert.True(await Paste(window, () => TextProvider("[000:00:02] [T2] short\r\n")));
            var current = Field<LogProjection>(window, "projection");
            Assert.Same(Field<LogData>(window, "data"), current.Source);
            Assert.Single(current.Source.Lines);
            Assert.Contains("short", editor.Text);
            Assert.Null(Field<TimeAnchor?>(window, "timeA"));
            Assert.Equal(0, (int?)Invoke(window, "CurrentSourceLine"));
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); }
    });

    private static Exception[] ProviderFailures() =>
    [
        new COMException("synthetic private payload", unchecked((int)0x800401D0)),
        new InvalidOperationException("synthetic private payload"),
        new NotSupportedException("synthetic private payload"),
        new SecurityException("synthetic private payload")
    ];
    private static SyntheticDataObject TextProvider(string text)
    {
        var provider = new SyntheticDataObject();
        provider.Payloads[DataFormats.UnicodeText] = text;
        return provider;
    }
    private static Task<bool> Paste(MainWindow window, Func<IDataObject?> provider) =>
        (Task<bool>)Invoke(window, "PasteFromDataObjectAsync", provider)!;
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] values) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, values);
    private static Task InSta(Func<Task> action) =>
        (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-clipboard-regression", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, true);
    }
    private sealed class SyntheticDataObject : IDataObject
    {
        public Dictionary<string, object> Payloads { get; } = new();
        public Exception? Failure { get; init; }
        public bool FailOnlyOnGetData { get; init; }
        public bool AutoConversionRequested { get; private set; }
        public object? GetData(string format, bool autoConvert)
        {
            AutoConversionRequested |= autoConvert;
            if (Failure is not null) throw Failure;
            return Payloads.GetValueOrDefault(format);
        }
        public object? GetData(string format) => GetData(format, true);
        public object? GetData(Type format) => GetData(format.FullName!, true);
        public bool GetDataPresent(string format, bool autoConvert)
        {
            AutoConversionRequested |= autoConvert;
            if (Failure is not null && !FailOnlyOnGetData) throw Failure;
            return Payloads.ContainsKey(format);
        }
        public bool GetDataPresent(string format) => GetDataPresent(format, true);
        public bool GetDataPresent(Type format) => GetDataPresent(format.FullName!, true);
        public string[] GetFormats(bool autoConvert) => Payloads.Keys.ToArray();
        public string[] GetFormats() => GetFormats(true);
        public void SetData(string format, object data, bool autoConvert) => Payloads[format] = data;
        public void SetData(string format, object data) => SetData(format, data, true);
        public void SetData(Type format, object data) => SetData(format.FullName!, data, true);
        public void SetData(object data) => SetData(data.GetType(), data);
    }
}
