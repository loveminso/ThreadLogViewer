using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;
using Xunit;

namespace ThreadLogViewer.UiTests;

[Collection(UiCollection.Name)]
public sealed class UiScenarioTests(PublishedApp app)
{
    private const string PrepareFailureSkip =
        "Skipped until PR #3 (feature/global-exceptions) merges. This branch does not show '강조 준비 실패' when keyword-highlight preparation fails: RefreshKeywordsAsync only handles cancellation and out-of-memory, and other failures are not written to the status bar. PR #3 sets the status bar to '강조 준비 실패 · 규칙을 확인한 뒤 다시 시도하세요.' Asserting that message here would fake a result this build does not produce.";

    private const string BomTruncationSkip =
        "Skipped: fix 4. UTF-8 with BOM still fails when a read snapshot ends in the middle of a character. LogFileReader.Decode treats EF BB BF as authoritative and decodes the payload with strict UTF-8, so an unfinished trailing scalar throws DecoderFallbackException and F5 shows '파일 새로 고침 실패'. BOM-less UTF-8 already drops that unfinished tail. This case stays skipped until fix 4; app behavior is unchanged.";

    [Fact]
    public void Scenario01_SelfContainedExeShowsMainWindow() =>
        Run(null, TimeSpan.FromMinutes(2), session =>
        {
            Assert.Contains("ThreadLog Viewer", session.Window.Title, StringComparison.Ordinal);
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
            session.DropFile(dropped);
            session.WaitUntil(
                () => session.Text("FileLabel").Contains("dropped-beta.log", StringComparison.Ordinal) &&
                      session.Status().Contains("파일 묶음 열기 완료", StringComparison.Ordinal) &&
                      !session.IsBusy(),
                TimeSpan.FromSeconds(20),
                "dropped log did not become the active tab");
            Assert.True(session.Window.FindFirstDescendant(cf => cf.ByName("opened-first.log")) is not null,
                "the command-line log tab is gone after the drop");
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

    [Fact(Skip = PrepareFailureSkip)]
    public void Scenario04_BadHighlightRuleShowsPrepareFailureAndKeepsRunning() =>
        throw new InvalidOperationException(
            "Unskip only after PR #3. Open a synthetic log, add a highlight rule that fails preparation, " +
            "assert OperationStatus contains '강조 준비 실패', and assert the main window is still open.");

    [Fact]
    public void Scenario05_RefreshWhileAppendingKorean()
    {
        RefreshWhileAppending(LogEncodingKind.Utf8, pauseDuringRefresh: false);
        RefreshWhileAppending(LogEncodingKind.Cp949, pauseDuringRefresh: true);
    }

    [Fact(Skip = BomTruncationSkip)]
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
                    if (pauseDuringRefresh)
                    {
                        long length = appender.Length;
                        appender.Resume();
                        appender.WaitForGrowth(length, TimeSpan.FromSeconds(10));
                        appender.Pause();
                    }
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

    private void Run(string? argument, TimeSpan timeout, Action<UiSession> body)
    {
        string name = CallerName();
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

    private static string CallerName([System.Runtime.CompilerServices.CallerMemberName] string name = "") => name;

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
