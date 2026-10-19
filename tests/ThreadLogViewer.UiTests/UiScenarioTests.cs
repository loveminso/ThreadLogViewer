using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using Xunit;

namespace ThreadLogViewer.UiTests;

[Collection(UiCollection.Name)]
public sealed class UiScenarioTests(PublishedApp app)
{
    [Fact]
    public void Scenario01_SelfContainedExeShowsMainWindow() =>
        Run(null, TimeSpan.FromMinutes(2), session =>
        {
            Assert.Equal(ExpectedAppTitle(session), session.Window.Title);
            Assert.False(session.App.HasExited);
            string exeDirectory = Path.GetDirectoryName(session.ExePath)!;
            Assert.True(File.Exists(Path.Combine(exeDirectory, "coreclr.dll")), "Self-contained publish is missing coreclr.dll.");
            Assert.True(File.Exists(Path.Combine(exeDirectory, "ThreadLogViewer.dll")));
            using var process = System.Diagnostics.Process.GetProcessById(session.App.ProcessId);
            string module = process.MainModule?.FileName ?? "";
            Assert.EndsWith("ThreadLogViewer.exe", module, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.GetFullPath(session.ExePath), Path.GetFullPath(module), ignoreCase: true);
            session.WaitUntil(() => session.Status().Length > 0, TimeSpan.FromSeconds(10), "status bar did not appear");
        });

    [Fact]
    public void Scenario02_OpenDropFilterAndExport()
    {
        string root = NewFolder("open-drop");
        string openedDir = Path.Combine(root, "opened");
        string droppedDir = Path.Combine(root, "dropped");
        string exportDir = Path.Combine(root, "export");
        Directory.CreateDirectory(openedDir);
        Directory.CreateDirectory(droppedDir);
        string opened = Path.Combine(openedDir, "opened-first.log");
        string dropped = Path.Combine(droppedDir, "dropped-beta.log");
        SyntheticLog.Write(opened, LogEncodingKind.Utf8, SyntheticLog.Line(1, 1, "opened-marker 합성열기"));
        SyntheticLog.Write(dropped, LogEncodingKind.Utf8, SyntheticLog.FilterLog());
        string[] openedBefore = Snapshot(openedDir);
        string[] droppedBefore = Snapshot(droppedDir);
        string openedHash = Hash(opened);
        string droppedHash = Hash(dropped);
        string destination = Path.Combine(exportDir, "filtered.log");
        Run(opened, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("opened-first.log");
            string appTitle = ExpectedAppTitle(session);
            Assert.Equal("opened-first.log \u2014 " + appTitle, session.Window.Title);
            session.DropFile(dropped);
            session.WaitUntil(
                () => session.Text("FileLabel").Contains("dropped-beta.log", StringComparison.Ordinal) &&
                      session.Status().Contains("파일 묶음 열기 완료", StringComparison.Ordinal) &&
                      !session.IsBusy(),
                TimeSpan.FromSeconds(20),
                "dropped log did not become the active tab");
            Assert.True(session.Window.FindFirstDescendant(cf => cf.ByName("opened-first.log")) is not null,
                "the command-line log tab is gone after the drop");
            Assert.Equal("dropped-beta.log \u2014 " + appTitle, session.Window.Title);
            session.ApplyIncludeFilter(SyntheticLog.KeepToken);
            Assert.Contains("1/", session.Text("CountStatus"), StringComparison.Ordinal);
            session.ExportTo(destination);
            string exported = File.ReadAllText(destination, Encoding.UTF8);
            Assert.Contains(SyntheticLog.KeepToken, exported, StringComparison.Ordinal);
            Assert.Contains(SyntheticLog.KeepKorean, exported, StringComparison.Ordinal);
            Assert.DoesNotContain(SyntheticLog.HideToken, exported, StringComparison.Ordinal);
            Assert.DoesNotContain(SyntheticLog.HideKorean, exported, StringComparison.Ordinal);
            Assert.DoesNotContain("\uFFFD", exported, StringComparison.Ordinal);
        });
        Assert.Equal(openedBefore, Snapshot(openedDir));
        Assert.Equal(droppedBefore, Snapshot(droppedDir));
        Assert.Equal(openedHash, Hash(opened));
        Assert.Equal(droppedHash, Hash(dropped));
        Assert.True(File.Exists(destination));
    }

    [Fact]
    public void Scenario03_SearchEnterAndShiftEnterMoveSelection()
    {
        string folder = NewFolder("search");
        string log = Path.Combine(folder, "search-zeta.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, SyntheticLog.SearchLog());
        Run(log, TimeSpan.FromMinutes(2), session =>
        {
            session.WaitForOpen("search-zeta.log");
            session.ShowSearch();
            session.SetSearchQuery(SyntheticLog.SearchToken);
            session.WaitForSearchStatus(status => status.Contains('개') && status.Contains('3'), "three search hits");
            PressSearch(session, shift: false);
            session.WaitForSearchStatus(status => status.Contains("1 / ", StringComparison.Ordinal), "first result");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 1줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "first hit position");
            string firstPosition = session.Text("PositionStatus");
            string firstSelected = SelectedResult(session);
            Assert.Contains("1줄:", firstSelected, StringComparison.Ordinal);
            PressSearch(session, shift: false);
            session.WaitForSearchStatus(status => status.Contains("2 / ", StringComparison.Ordinal), "next result");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 2줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "next hit position");
            string secondPosition = session.Text("PositionStatus");
            string secondSelected = SelectedResult(session);
            Assert.NotEqual(firstPosition, secondPosition);
            Assert.NotEqual(firstSelected, secondSelected);
            Assert.Contains("2줄:", secondSelected, StringComparison.Ordinal);
            PressSearch(session, shift: true);
            session.WaitForSearchStatus(status => status.Contains("1 / ", StringComparison.Ordinal), "previous result");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 1줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "previous hit position");
            Assert.Contains("1줄:", SelectedResult(session), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Scenario04_BadHighlightRuleShowsPrepareFailureAndKeepsRunning()
    {
        string folder = NewFolder("highlight");
        string log = Path.Combine(folder, "highlight.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, SyntheticLog.Line(1, 1, "synthetic highlight keep-phrase"));
        string hash = Hash(log);
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("highlight.log");
            UiFeatureTests.SelectSearch(session, "keep-phrase");
            UiFeatureTests.ToggleHighlight(session);
            session.WaitUntil(() =>
            {
                string status = session.Status();
                return status.Contains("문구 강조 추가", StringComparison.Ordinal) &&
                       status.Contains("1/8", StringComparison.Ordinal) &&
                       !status.Contains("강조 준비 실패", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(15), "valid highlight was not applied");
            AddUnpreparedHighlightRule(session, expectedCount: 2);
            session.WaitUntil(() =>
            {
                string status = session.Status();
                return status.Contains("강조 준비 실패", StringComparison.Ordinal) &&
                       status.Contains("broken", StringComparison.Ordinal) &&
                       status.Contains("2번 보라", StringComparison.Ordinal) &&
                       status.Contains("1곳", StringComparison.Ordinal) &&
                       !status.Contains("강조 준비 중", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(15), "bad rule skipped while the valid phrase stayed highlighted");
            Assert.False(session.App.HasExited);
            Assert.Contains("ThreadLog Viewer", session.Window.Title, StringComparison.Ordinal);
            Assert.Contains("highlight.log", session.Text("FileLabel"), StringComparison.Ordinal);
            Assert.DoesNotContain("\uD800", session.Status(), StringComparison.Ordinal);
            UiFeatureTests.AssertRegisteredPhrase(session, "keep-phrase");
        });
        Assert.Equal(hash, Hash(log));
    }

    [Fact]
    public void Scenario14_HighlightAndHelpWindowsFitWorkArea()
    {
        string folder = NewFolder("fit");
        string log = Path.Combine(folder, "fit.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, SyntheticLog.Line(1, 1, "synthetic layout fit-phrase"));
        string hash = Hash(log);
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("fit.log");
            PlaceMainWindow(session);
            Window highlight = OpenHighlightManager(session);
            try
            {
                PlaceMainWindow(session);
                Thread.Sleep(300);
                AssertInsideWorkArea(highlight, "highlight manager");
                AssertButtonInside(highlight, "HighlightApplyButton", "적용");
                AssertButtonInside(highlight, null, "취소");
            }
            finally
            {
                CloseNamedButton(highlight, "취소");
            }
            session.WaitUntil(() => HighlightDialog(session) is null, TimeSpan.FromSeconds(8), "highlight manager stayed open");
            session.Press(VirtualKeyShort.F1);
            Window? help = null;
            session.WaitUntil(() => (help = HelpDialog(session)) is not null, TimeSpan.FromSeconds(8), "help window did not open");
            Window about = help ?? throw new Xunit.Sdk.XunitException("help window did not open");
            try
            {
                AssertInsideWorkArea(about, "help");
                AssertButtonInside(about, "HelpOkButton", "확인");
            }
            finally
            {
                CloseNamedButton(about, "확인");
            }
        });
        Assert.Equal(hash, Hash(log));
    }

    [Fact]
    public void Scenario05_RefreshWhileAppendingKorean()
    {
        RefreshWhileAppending(LogEncodingKind.Utf8, pauseDuringRefresh: false);
        RefreshWhileAppending(LogEncodingKind.Cp949, pauseDuringRefresh: true);
    }

    [Fact]
    public void Scenario05_Utf8Bom_RefreshWhileAppending() =>
        RefreshWhileAppending(LogEncodingKind.Utf8Bom, pauseDuringRefresh: false);

    [Fact]
    public void Scenario06_OpenedLogDirectoryGainsNoFiles()
    {
        string folder = NewFolder("clean");
        string exportDir = NewFolder("clean-export");
        string log = Path.Combine(folder, "clean.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, SyntheticLog.MixedLog());
        string[] before = Snapshot(folder);
        string hash = Hash(log);
        string destination = Path.Combine(exportDir, "clean-filtered.log");
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("clean.log");
            session.ApplyIncludeFilter(SyntheticLog.KeepToken);
            session.ShowSearch();
            session.SetSearchQuery(SyntheticLog.SearchToken);
            session.WaitForSearchStatus(status => status.Contains('개'), "search hit on the kept line");
            PressSearch(session, shift: false);
            session.WaitForSearchStatus(status => status.Contains(" / ", StringComparison.Ordinal), "search navigation");
            session.RefreshAndWait();
            session.ExportTo(destination);
            Assert.False(session.App.HasExited);
        });
        Assert.Equal(before, Snapshot(folder));
        Assert.Equal(hash, Hash(log));
        string exported = File.ReadAllText(destination, Encoding.UTF8);
        Assert.Contains(SyntheticLog.KeepKorean, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(SyntheticLog.HideKorean, exported, StringComparison.Ordinal);
        Assert.StartsWith(exportDir, Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshWhileAppending(LogEncodingKind kind, bool pauseDuringRefresh)
    {
        string folder = NewFolder("refresh-" + kind);
        string log = Path.Combine(folder, "append.log");
        SyntheticLog.Write(log, kind, SyntheticLog.RefreshSeed());
        string[] before = Snapshot(folder);
        long seeded = new FileInfo(log).Length;
        UiSta.Run(() =>
        {
            LogAppender? appender = null;
            UiSession? session = null;
            try
            {
                if (pauseDuringRefresh)
                    appender = LogAppender.Start(log, kind, startPaused: true);
                session = UiSession.Launch(app.ExePath, log, app.ScreenshotDirectory, "Scenario05_" + kind);
                session.WaitForOpen("append.log");
                if (appender is null)
                {
                    appender = LogAppender.Start(log, kind, startPaused: false);
                    appender.WaitForGrowth(seeded, TimeSpan.FromSeconds(10));
                }
                for (int iteration = 1; iteration <= 20; iteration++)
                {
                    long length = appender.Length;
                    if (pauseDuringRefresh)
                        appender.Resume();
                    appender.WaitForGrowth(length, TimeSpan.FromSeconds(10));
                    if (pauseDuringRefresh)
                        appender.Pause();
                    session.RefreshAndWait();
                    session.Step(kind + " F5 " + iteration);
                }
                string encoding = session.Text("EncodingStatus");
                if (kind == LogEncodingKind.Cp949)
                    Assert.Contains("CP949", encoding, StringComparison.Ordinal);
                else
                    Assert.Contains("UTF-8", encoding, StringComparison.Ordinal);
                AssertKnownKorean(session);
                Assert.False(session.App.HasExited);
            }
            catch (Exception ex)
            {
                session?.CaptureFailure(ex);
                throw;
            }
            finally
            {
                session?.Dispose();
                appender?.Dispose();
            }
        }, TimeSpan.FromMinutes(4));
        Assert.Equal(before, Snapshot(folder));
    }

    private static void AssertKnownKorean(UiSession session)
    {
        session.ShowSearch();
        AssertHits(session, SyntheticLog.OriginalKorean);
        AssertHits(session, SyntheticLog.Distinctive);
        AssertHits(session, SyntheticLog.AppendMarker);
        session.SetSearchQuery("\uFFFD");
        string status = session.WaitForSearchStatus(value => value.Contains("결과 없음", StringComparison.Ordinal), "replacement character search");
        Assert.Contains("결과 없음", status, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFD", session.Status(), StringComparison.Ordinal);
    }

    private static void AssertHits(UiSession session, string query)
    {
        session.SetSearchQuery(query);
        string status = session.WaitForSearchStatus(
            value => value.Contains('개') && !value.Contains("결과 없음", StringComparison.Ordinal),
            "hits for " + query);
        Assert.DoesNotContain("결과 없음", status, StringComparison.Ordinal);
    }

    private static string ExpectedAppTitle(UiSession session)
    {
        var info = FileVersionInfo.GetVersionInfo(session.ExePath);
        return $"ThreadLog Viewer v{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
    }

    private void Run(string? argument, TimeSpan timeout, Action<UiSession> body, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        UiSta.Run(() =>
        {
            using var session = UiSession.Launch(app.ExePath, argument, app.ScreenshotDirectory, name);
            try
            {
                body(session);
            }
            catch (Exception ex)
            {
                session.CaptureFailure(ex);
                throw;
            }
        }, timeout);
    }

    private static void AddUnpreparedHighlightRule(UiSession session, int expectedCount = 1)
    {
        Window dialog = OpenHighlightManager(session);
        var box = dialog.FindFirstDescendant(cf => cf.ByAutomationId("HighlightPendingBox"))?.AsTextBox()
            ?? throw new Xunit.Sdk.XunitException("Highlight phrase box was not found.");
        const string badPhrase = "broken\uD800";
        box.Text = badPhrase;
        session.WaitUntil(() => (box.Text ?? "").Contains('\uD800'), TimeSpan.FromSeconds(5), "bad highlight phrase was not entered");
        var add = dialog.FindFirstDescendant(cf => cf.ByAutomationId("HighlightAddButton"))?.AsButton()
            ?? throw new Xunit.Sdk.XunitException("Highlight add button was not found.");
        session.WaitUntil(() => add.IsEnabled, TimeSpan.FromSeconds(5), "highlight add button stayed disabled");
        add.Invoke();
        string registered = expectedCount + "/8";
        session.WaitUntil(
            () => dialog.FindAllDescendants().Any(element => (element.Name ?? "").Contains(registered, StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "bad highlight rule was not registered");
        var apply = dialog.FindFirstDescendant(cf => cf.ByAutomationId("HighlightApplyButton"))?.AsButton()
            ?? throw new Xunit.Sdk.XunitException("Highlight apply button was not found.");
        apply.Invoke();
        session.Step("applied unpaired-surrogate highlight rule");
    }

    private static Window OpenHighlightManager(UiSession session)
    {
        session.FocusWindow();
        var menus = session.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
        var analysis = menus.FirstOrDefault(item =>
        {
            string name = item.Name ?? "";
            return name.Equals("분석", StringComparison.Ordinal) || name.StartsWith("분석(", StringComparison.Ordinal);
        }) ?? throw new Xunit.Sdk.XunitException("Analysis menu was not found. " + session.Describe());
        var menu = analysis.AsMenuItem();
        menu.Expand();
        FlaUI.Core.Input.Wait.UntilInputIsProcessed();
        AutomationElement? manage = null;
        var opened = System.Diagnostics.Stopwatch.StartNew();
        while (manage is null && opened.Elapsed < TimeSpan.FromSeconds(5))
        {
            manage = menu.Items.FirstOrDefault(item => (item.Name ?? "").Contains("강조 문구 관리", StringComparison.Ordinal));
            manage ??= session.Window.FindFirstDescendant(cf => cf.ByName("강조 문구 관리…"));
            if (manage is null)
                Thread.Sleep(50);
        }
        if (manage is null)
            throw new Xunit.Sdk.XunitException("Highlight manager item was not found. " + string.Join(" | ", menu.Items.Select(item => item.Name)));
        manage.Click();
        FlaUI.Core.Input.Wait.UntilInputIsProcessed();
        Window? foundDialog = null;
        session.WaitUntil(() => (foundDialog = HighlightDialog(session)) is not null, TimeSpan.FromSeconds(10), "highlight manager did not open");
        return foundDialog ?? throw new Xunit.Sdk.XunitException("Highlight manager did not open.");
    }

    private static void PlaceMainWindow(UiSession session)
    {
        var hwnd = session.Window.Properties.NativeWindowHandle.Value;
        var work = UiReviewCapture.WorkArea(hwnd);
        // The CI desktop is 1024x768 with a taskbar, so the work area is shorter than 768.
        int width = Math.Min(1024, work.Width);
        int height = Math.Min(768, work.Height);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            UiReviewCapture.MoveWindow(hwnd, work.Left, work.Top, width, height);
            Thread.Sleep(200);
            var actual = UiReviewCapture.WindowSize(hwnd);
            if (Math.Abs(actual.Width - width) <= 24 && Math.Abs(actual.Height - height) <= 24)
                return;
        }
        var last = UiReviewCapture.WindowSize(hwnd);
        throw new Xunit.Sdk.XunitException($"Main window stayed {last.Width}x{last.Height} instead of {width}x{height} inside work area {work.Width}x{work.Height}.");
    }

    private static void AssertInsideWorkArea(Window window, string label)
    {
        var bounds = window.BoundingRectangle;
        var work = UiReviewCapture.WorkArea(window.Properties.NativeWindowHandle.Value);
        const int slack = 8;
        Assert.True(bounds.Width > 1 && bounds.Height > 1, label + " has no size");
        Assert.True(
            bounds.Left >= work.Left - slack && bounds.Top >= work.Top - slack &&
            bounds.Right <= work.Right + slack && bounds.Bottom <= work.Bottom + slack,
            $"{label} bounds {bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height} are outside work area {work.Left},{work.Top} {work.Width}x{work.Height}");
    }

    private static void AssertButtonInside(Window window, string? automationId, string name)
    {
        var element = automationId is null
            ? window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
            : window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        if (element is null)
            throw new Xunit.Sdk.XunitException(name + " button was not found.");
        Assert.False(element.IsOffscreen, name + " is offscreen");
        var bounds = element.BoundingRectangle;
        var work = UiReviewCapture.WorkArea(window.Properties.NativeWindowHandle.Value);
        const int slack = 8;
        Assert.True(bounds.Width > 1 && bounds.Height > 1, name + " has no size");
        Assert.True(
            bounds.Left >= work.Left - slack && bounds.Top >= work.Top - slack &&
            bounds.Right <= work.Right + slack && bounds.Bottom <= work.Bottom + slack,
            $"{name} bounds {bounds.Left},{bounds.Top} {bounds.Width}x{bounds.Height} are outside work area {work.Left},{work.Top} {work.Width}x{work.Height}");
    }

    private static void CloseNamedButton(Window window, string name)
    {
        try
        {
            window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))?.AsButton().Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }
    }

    private static Window? HelpDialog(UiSession session)
    {
        foreach (var window in OwnedWindows(session).Concat(TopLevelWindows(session)))
        {
            try
            {
                if ((window.Title ?? "").Contains("단축키", StringComparison.Ordinal))
                    return window;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
            }
        }
        return null;
    }

    private static Window? HighlightDialog(UiSession session)
    {
        foreach (var window in OwnedWindows(session).Concat(TopLevelWindows(session)))
        {
            if (string.Equals(window.AutomationId, "HighlightManagerWindow", StringComparison.Ordinal) ||
                (window.Title ?? "").Contains("강조 문구 관리", StringComparison.Ordinal))
                return window;
        }
        return null;
    }

    private static IEnumerable<Window> OwnedWindows(UiSession session)
    {
        var windows = new List<Window>();
        try
        {
            windows.AddRange(session.Window.ModalWindows);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }
        try
        {
            windows.AddRange(session.Window.FindAllChildren(cf => cf.ByControlType(ControlType.Window)).Select(element => element.AsWindow()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }
        return windows;
    }

    private static IEnumerable<Window> TopLevelWindows(UiSession session)
    {
        try
        {
            return session.App.GetAllTopLevelWindows(session.Automation);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return [];
        }
    }

    private static void PressSearch(UiSession session, bool shift)
    {
        session.FocusWindow();
        var box = session.Require("SearchBox");
        box.Focus();
        box.Click();
        FlaUI.Core.Input.Wait.UntilInputIsProcessed();
        Thread.Sleep(80);
        if (shift)
            FlaUI.Core.Input.Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        else
            FlaUI.Core.Input.Keyboard.Type(VirtualKeyShort.RETURN);
        FlaUI.Core.Input.Wait.UntilInputIsProcessed();
    }

    private static string SelectedResult(UiSession session)
    {
        var selected = session.Require("ResultsList").AsListBox().SelectedItem;
        string text = selected?.Text ?? "";
        if (text.Length == 0)
            text = selected?.Name ?? "";
        if (text.Length == 0)
            throw new Xunit.Sdk.XunitException("No selected search result. " + session.Describe());
        return text;
    }

    private static string NewFolder(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "tlv-ui-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string[] Snapshot(string directory) =>
        Directory.GetFileSystemEntries(directory)
            .Select(entry => Path.GetFileName(entry) ?? entry)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

    private static string Hash(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
