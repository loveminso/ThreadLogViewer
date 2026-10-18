using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace ThreadLogViewer.UiTests;

internal sealed class UiSession : IDisposable
{
    private static readonly object Gate = new();
    private static UiSession? active;
    private readonly List<string> trace = [];
    private bool disposed;

    public Application App { get; }
    public UIA3Automation Automation { get; }
    public Window Window { get; }
    public string ScreenshotDirectory { get; }
    public string Name { get; }
    public string ExePath { get; }

    private UiSession(Application app, UIA3Automation automation, Window window, string screenshotDirectory, string name, string exePath)
    {
        App = app;
        Automation = automation;
        Window = window;
        ScreenshotDirectory = screenshotDirectory;
        Name = name;
        ExePath = exePath;
    }

    public static UiSession Launch(string exePath, string? argument, string screenshotDirectory, string name)
    {
        var start = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)!
        };
        if (!string.IsNullOrEmpty(argument))
            start.ArgumentList.Add(argument);
        var automation = new UIA3Automation();
        Application? app = null;
        try
        {
            app = Application.Launch(start);
            var window = app.GetMainWindow(automation, TimeSpan.FromSeconds(45))
                ?? throw new InvalidOperationException("Main window did not appear.");
            var session = new UiSession(app, automation, window, screenshotDirectory, name, exePath);
            lock (Gate) active = session;
            session.PlaceOnScreen();
            session.Step("launched " + exePath + (argument is null ? "" : " " + argument));
            return session;
        }
        catch
        {
            lock (Gate) active = null;
            try { app?.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            automation.Dispose();
            throw;
        }
    }

    public static void CaptureActive(Exception exception)
    {
        UiSession? session;
        lock (Gate) session = active;
        session?.CaptureFailure(exception);
    }

    public static void AbandonActive()
    {
        UiSession? session;
        lock (Gate) session = active;
        session?.Dispose();
    }

    public void Step(string message) => trace.Add($"{DateTime.UtcNow:HH:mm:ss.fff} {message}");

    public AutomationElement Require(string automationId, TimeSpan? timeout = null)
    {
        var found = Retry.WhileNull(
            () => SafeFind(automationId),
            timeout ?? TimeSpan.FromSeconds(15),
            TimeSpan.FromMilliseconds(50)).Result;
        if (found is null)
            throw new InvalidOperationException($"AutomationId '{automationId}' was not found. {Describe()}");
        return found;
    }

    public string Text(string automationId) => ReadDisplayedText(Require(automationId));

    public string Status()
    {
        var element = Require("OperationStatus");
        string displayed = ReadDisplayedText(element);
        if (displayed == "최근 작업 안내")
        {
            string help = element.HelpText ?? "";
            if (help.Length > 0)
                return help;
        }
        return displayed;
    }

    public bool IsBusy()
    {
        var work = SafeFind("WorkStatus");
        if (work is null || work.IsOffscreen)
            return false;
        var bounds = work.BoundingRectangle;
        return bounds.Width > 1 && bounds.Height > 1;
    }

    public void WaitUntil(Func<bool> condition, TimeSpan timeout, string description)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            ThrowIfErrorDialog();
            if (condition())
                return;
            Thread.Sleep(40);
        }
        throw new Xunit.Sdk.XunitException($"{description} {Describe()}");
    }

    public void WaitForOpen(string fileName)
    {
        WaitUntil(
            () => Text("FileLabel").Contains(fileName, StringComparison.Ordinal) &&
                  (Status().Contains("파일 묶음 열기 완료", StringComparison.Ordinal) || Status().Contains("열기 완료", StringComparison.Ordinal)) &&
                  !IsBusy(),
            TimeSpan.FromSeconds(30),
            "file did not finish opening: " + fileName);
        Step("opened " + fileName);
    }

    public void FocusWindow()
    {
        ThrowIfErrorDialog();
        Window.Focus();
        Wait.UntilInputIsProcessed();
    }

    public void Press(params VirtualKeyShort[] keys)
    {
        FocusWindow();
        if (keys.Length == 1)
            Keyboard.Type(keys[0]);
        else
            Keyboard.TypeSimultaneously(keys);
        Wait.UntilInputIsProcessed();
    }

    public void RefreshAndWait()
    {
        FocusWindow();
        string? beforeStatus = TryStatus();
        string? beforeCount = TryDisplayed("CountStatus");
        bool sawBusy = false;
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            Keyboard.Type(VirtualKeyShort.F5);
            var watch = Stopwatch.StartNew();
            var budget = attempt == 1 ? TimeSpan.FromSeconds(12) : TimeSpan.FromSeconds(20);
            while (watch.Elapsed < budget)
            {
                ThrowIfErrorDialog();
                if (IsBusy())
                    sawBusy = true;
                string? status = TryStatus();
                if (status is null)
                {
                    Thread.Sleep(5);
                    continue;
                }
                if (status.Contains("파일 새로 고침 실패", StringComparison.Ordinal))
                    throw new Xunit.Sdk.XunitException("Refresh failed. " + Describe());
                string? count = TryDisplayed("CountStatus");
                bool finished = status.Contains("파일 새로 고침 완료", StringComparison.Ordinal) && !IsBusy();
                bool statusChanged = beforeStatus is not null && status != beforeStatus;
                bool countChanged = count is not null && beforeCount is not null && count != beforeCount;
                if (finished && (sawBusy || statusChanged || countChanged))
                {
                    Step("refresh completed");
                    return;
                }
                Thread.Sleep(5);
            }
            Step("refresh attempt " + attempt + " sawBusy=" + sawBusy);
        }
        throw new Xunit.Sdk.XunitException("Refresh did not finish. sawBusy=" + sawBusy + " " + Describe());
    }

    public void ShowSearch()
    {
        Press(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_F);
        Require("SearchBox");
        Step("search open");
    }

    public void SetSearchQuery(string query)
    {
        var box = Require("SearchBox").AsTextBox();
        box.Text = "";
        WaitUntil(() => Text("SearchStatus").Length == 0, TimeSpan.FromSeconds(10), "search did not clear");
        box.Text = query;
        Step("search " + query);
    }

    public string WaitForSearchStatus(Func<string, bool> predicate, string description)
    {
        string seen = "";
        WaitUntil(() =>
        {
            seen = Text("SearchStatus");
            return seen.Length > 0 && !seen.Contains("검색 중", StringComparison.Ordinal) && predicate(seen);
        }, TimeSpan.FromSeconds(15), description + " last='" + seen + "'");
        return seen;
    }

    public void ApplyIncludeFilter(string phrase)
    {
        var expander = Require("ContentFilterExpander");
        if (expander.Patterns.ExpandCollapse.TryGetPattern(out var expand))
            expand.Expand();
        else
            expander.Click();
        var include = Require("IncludeBox").AsTextBox();
        include.Text = phrase;
        var apply = Require("ApplyFilterButton").AsButton();
        WaitUntil(() => apply.IsEnabled, TimeSpan.FromSeconds(10), "filter button stayed disabled");
        apply.Invoke();
        WaitUntil(
            () => Status().Contains("필터 적용 완료", StringComparison.Ordinal) && !IsBusy(),
            TimeSpan.FromSeconds(20),
            "filter did not finish");
        Step("filter " + phrase);
    }

    public void ExportTo(string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var export = Require("ExportButton").AsButton();
        WaitUntil(() => export.IsEnabled && !IsBusy(), TimeSpan.FromSeconds(15), "export button stayed disabled");
        export.Invoke();
        var dialog = WaitForTopLevel(window => (window.Title ?? "").Contains("내보내기", StringComparison.Ordinal), TimeSpan.FromSeconds(15), "export dialog");
        Step("export dialog " + dialog.Title);
        dialog.Focus();
        SetDialogFileName(dialog, destination);
        InvokeSave(dialog);
        WaitUntil(
            () => Status().Contains("내보내기 완료", StringComparison.Ordinal) && File.Exists(destination),
            TimeSpan.FromSeconds(20),
            "export did not finish");
        Step("exported " + destination);
    }

    public void DropFile(string path)
    {
        string fileName = Path.GetFileName(path);
        PlaceOnScreen();
        string lastEffect = "";
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            FocusWindow();
            var hwnd = Window.Properties.NativeWindowHandle.Value;
            SetWindowPos(hwnd, new IntPtr(-1), 40, 40, 1200, 800, 0x0040);
            lastEffect = OleFileDrop.Drop(hwnd, path, Step);
            SetWindowPos(hwnd, new IntPtr(-2), 40, 40, 1200, 800, 0x0040);
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(12))
            {
                ThrowIfErrorDialog();
                if (Text("FileLabel").Contains(fileName, StringComparison.Ordinal) && !IsBusy())
                {
                    Step("drop landed on attempt " + attempt);
                    return;
                }
                Thread.Sleep(100);
            }
            Step("drop attempt " + attempt + " did not open " + fileName);
            Press(VirtualKeyShort.ESCAPE);
        }
        throw new Xunit.Sdk.XunitException($"OLE file drop did not open {fileName}. lastEffect={lastEffect} {Describe()}");
    }

    public IReadOnlyList<string> Trace => trace;

    public void CaptureFailure(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(ScreenshotDirectory);
            string safe = string.Join("_", Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string png = Path.Combine(ScreenshotDirectory, safe + ".png");
            string note = Path.Combine(ScreenshotDirectory, safe + ".txt");
            try
            {
                Capture.MainScreen().ToFile(png);
            }
            catch (Exception captureException) when (captureException is not OutOfMemoryException)
            {
                File.WriteAllText(png + ".error.txt", captureException.ToString());
            }
            File.WriteAllText(note, exception + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, trace) + Environment.NewLine + Describe());
        }
        catch (Exception captureException) when (captureException is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        lock (Gate)
        {
            if (ReferenceEquals(active, this))
                active = null;
        }
        try { OleFileDrop.ReleasePointer(); } catch (Exception ex) when (ex is InvalidOperationException or EntryPointNotFoundException) { }
        try
        {
            if (!App.HasExited)
            {
                App.CloseTimeout = TimeSpan.FromSeconds(5);
                App.Close();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        try
        {
            if (!App.HasExited)
                App.Kill();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        App.Dispose();
        Automation.Dispose();
    }

    private void PlaceOnScreen()
    {
        var hwnd = Window.Properties.NativeWindowHandle.Value;
        ShowWindow(hwnd, 9);
        SetWindowPos(hwnd, IntPtr.Zero, 40, 40, 1200, 800, 0x0040);
        SetForegroundWindow(hwnd);
        Window.Focus();
    }

    private string? TryStatus()
    {
        var element = SafeFind("OperationStatus");
        if (element is null)
            return null;
        try
        {
            string displayed = ReadDisplayedText(element);
            if (displayed == "최근 작업 안내")
            {
                string help = element.HelpText ?? "";
                if (help.Length > 0)
                    return help;
            }
            return displayed;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private string? TryDisplayed(string automationId)
    {
        var element = SafeFind(automationId);
        if (element is null)
            return null;
        try { return ReadDisplayedText(element); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private AutomationElement? SafeFind(string automationId)
    {
        try
        {
            if (App.HasExited)
                return null;
            return Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or TimeoutException)
        {
            return null;
        }
    }

    private void ThrowIfErrorDialog()
    {
        if (App.HasExited)
            throw new Xunit.Sdk.XunitException("Viewer process exited. " + Describe());
        string? title = ErrorDialogTitle();
        if (title is not null)
            throw new Xunit.Sdk.XunitException("Error dialog: " + title + " " + Describe());
    }

    private string? ErrorDialogTitle()
    {
        Window[] windows;
        try { windows = App.GetAllTopLevelWindows(Automation); }
        catch (Exception ex) when (ex is InvalidOperationException or COMException) { return null; }
        var main = Window.Properties.NativeWindowHandle.Value;
        foreach (var window in windows)
        {
            if (window.Properties.NativeWindowHandle.ValueOrDefault == main)
                continue;
            string title = window.Title ?? "";
            if (title.Contains("실패", StringComparison.Ordinal) ||
                title.Contains("오류", StringComparison.Ordinal) ||
                title.Contains("열 수 없습니다", StringComparison.Ordinal))
                return title;
        }
        return null;
    }

    private Window WaitForTopLevel(Func<Window, bool> match, TimeSpan timeout, string description)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            Window[] windows;
            try { windows = App.GetAllTopLevelWindows(Automation); }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                windows = [];
            }
            foreach (var window in windows)
            {
                try
                {
                    if (match(window))
                        return window;
                }
                catch (COMException) { }
            }
            Thread.Sleep(80);
        }
        throw new Xunit.Sdk.XunitException(description + " did not appear. " + Describe());
    }

    private static void SetDialogFileName(Window dialog, string destination)
    {
        var box = dialog.FindFirstDescendant(cf => cf.ByAutomationId("FileNameControl"))
            ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("1001"))
            ?? dialog.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Edit)).LastOrDefault()
            ?? throw new InvalidOperationException("Export dialog has no file-name field. title=" + dialog.Title);
        if (!box.Patterns.Value.TryGetPattern(out var value))
            box.AsTextBox().Text = destination;
        else
            value.SetValue(destination);
    }

    private static void InvokeSave(Window dialog)
    {
        var save = dialog.FindFirstDescendant(cf => cf.ByAutomationId("1"))
            ?? dialog.FindFirstDescendant(cf => cf.ByName("Save"))
            ?? dialog.FindFirstDescendant(cf => cf.ByName("저장"))
            ?? throw new InvalidOperationException("Export dialog has no Save button. title=" + dialog.Title);
        save.AsButton().Invoke();
    }

    private string Describe()
    {
        string status = Safe(() => Status());
        string file = Safe(() => Text("FileLabel"));
        string count = Safe(() => Text("CountStatus"));
        string encoding = Safe(() => Text("EncodingStatus"));
        return $"status='{status}' file='{file}' count='{count}' encoding='{encoding}' busy={SafeBool(IsBusy)} exited={SafeBool(() => App.HasExited)}";
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return "<" + ex.GetType().Name + ">"; }
    }

    private static string SafeBool(Func<bool> read)
    {
        try { return read().ToString(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return ex.GetType().Name; }
    }

    private static string ReadDisplayedText(AutomationElement element)
    {
        if (element.Patterns.Text.TryGetPattern(out var text))
        {
            try
            {
                string value = text.DocumentRange.GetText(16_384) ?? "";
                if (value.Length > 0)
                    return value.Trim();
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
            {
            }
        }
        string help = element.HelpText ?? "";
        if (help.Length > 0 && help != "최근 작업 안내")
            return help.Trim();
        return (element.Name ?? "").Trim();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
