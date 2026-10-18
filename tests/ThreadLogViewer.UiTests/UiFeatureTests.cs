using System.Security.Cryptography;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Xunit;

namespace ThreadLogViewer.UiTests;

[Collection(UiCollection.Name)]
public sealed class UiFeatureTests(PublishedApp app)
{
    private const string AnalysisLog =
        "[014:15:50.113] [T 3] Read start · error ERROR error_code terror · A한🙂 (synthetic.cpp:100)\n" +
        "  timeout · dump=00 01 02 03\n" +
        "  checksum=OK\n" +
        "[014:15:50.125] [T 7] Write complete (synthetic.cpp:200)\n" +
        "[014:15:50.200] [T 3] heartbeat (synthetic.cpp:300)\n" +
        "  timeout\n" +
        "[014:99:00] [T 11] invalid timestamp (synthetic.cpp:400)\n";

    [Fact]
    public void Scenario07_JumpToOriginalLine()
    {
        string folder = NewFolder("goto");
        string log = Path.Combine(folder, "line-mapping.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, MappingLog());
        string hash = Hash(log);
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("line-mapping.log");
            ShowOnlyThread(session, "T 3");
            session.WaitUntil(() => session.Text("CountStatus").Contains("표시 2/", StringComparison.Ordinal), TimeSpan.FromSeconds(15), "T 3 filter did not leave two lines");
            Assert.Contains("2/110", session.Text("CountStatus"), StringComparison.Ordinal);
            GoTo(session, "105");
            session.WaitUntil(
                () => session.Text("GoToStatus").Contains("원본 105줄로 이동", StringComparison.Ordinal) &&
                      session.Text("PositionStatus").Contains("원본 105줄", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10),
                "goto 105");
            Keyboard.Type(VirtualKeyShort.UP);
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 100줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "line above 105");
            Keyboard.Type(VirtualKeyShort.DOWN);
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 105줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "back to the second displayed line");
            Assert.Contains("표시 2/", session.Text("CountStatus"), StringComparison.Ordinal);
            GoTo(session, "101");
            session.WaitUntil(() => session.Text("GoToStatus").Contains("필터로 숨겨진 줄입니다", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "hidden original line");
                var hidden = session.Require("HiddenContextButton");
                Assert.False(hidden.IsOffscreen, session.Describe());
            Assert.Contains("원본 105줄", session.Text("PositionStatus"), StringComparison.Ordinal);
            foreach (string rejected in new[] { "0", "111", "abc" })
            {
                GoTo(session, rejected);
                session.WaitUntil(
                    () => session.Text("GoToStatus").Contains("1~110의 원본 줄 번호를 입력하세요.", StringComparison.Ordinal),
                    TimeSpan.FromSeconds(10),
                    "rejected " + rejected);
                Assert.Contains("원본 105줄", session.Text("PositionStatus"), StringComparison.Ordinal);
                Assert.Contains("표시 2/", session.Text("CountStatus"), StringComparison.Ordinal);
                var hiddenAfter = Find(session, "HiddenContextButton");
                Assert.True(hiddenAfter is null || hiddenAfter.IsOffscreen, "hidden-line button stayed up after " + rejected);
            }
            Assert.False(session.App.HasExited);
        });
        Assert.Equal(hash, Hash(log));
    }

    [Fact]
    public void Scenario08_PositionAndSelectionInfo()
    {
        string folder = NewFolder("selection");
        string log = Path.Combine(folder, "line-mapping.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, MappingLog());
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("line-mapping.log");
            ShowOnlyThread(session, "T 3");
            session.WaitUntil(() => session.Text("CountStatus").Contains("표시 2/", StringComparison.Ordinal), TimeSpan.FromSeconds(15), "two displayed lines");
            session.ShowSearch();
            session.SetSearchQuery("A한🙂");
            session.WaitForSearchStatus(status => status.Contains('개') && !status.Contains("결과 없음", StringComparison.Ordinal), "emoji search hit");
            PressSearch(session, shift: false);
            session.WaitUntil(() =>
            {
                string position = session.Text("PositionStatus");
                return position.Contains("선택 1줄", StringComparison.Ordinal) && position.Contains(" / 3자", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10), "A한🙂 scalar count");
            Assert.DoesNotContain("4자", session.Text("PositionStatus"), StringComparison.Ordinal);
            Assert.Contains("원본 100줄", session.Text("PositionStatus"), StringComparison.Ordinal);
            GoTo(session, "100");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("읽기 전용", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "goto cleared the selection");
            Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.DOWN);
            session.WaitUntil(() =>
            {
                string position = session.Text("PositionStatus");
                return position.Contains("원본 105줄", StringComparison.Ordinal) && position.Contains("선택 1줄", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10), "selection ending at the next line");
            Assert.DoesNotContain("선택 2줄", session.Text("PositionStatus"), StringComparison.Ordinal);
            Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
            session.WaitUntil(() => session.Text("PositionStatus").Contains("선택 2줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "both displayed lines");
            Assert.DoesNotContain("6줄", session.Text("PositionStatus"), StringComparison.Ordinal);
            Assert.Contains("표시 2/", session.Text("CountStatus"), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Scenario09_ValidHighlightRuleHighlightsPhrase()
    {
        string folder = NewFolder("highlight-ok");
        string log = Path.Combine(folder, "analysis.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, AnalysisLog);
        string hash = Hash(log);
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("analysis.log");
            string count = session.Text("CountStatus");
            SelectSearch(session, "timeout");
            ToggleHighlight(session);
            session.WaitUntil(() =>
            {
                string status = session.Status();
                return status.Contains("문구 강조 추가", StringComparison.Ordinal) &&
                       status.Contains("1/8", StringComparison.Ordinal) &&
                       status.Contains("주황", StringComparison.Ordinal) &&
                       !status.Contains("강조 준비", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(15), "timeout highlight was not applied");
            AssertRegisteredPhrase(session, "timeout");
            SelectSearch(session, "Write");
            ToggleHighlight(session);
            session.WaitUntil(() =>
            {
                string status = session.Status();
                return status.Contains("문구 강조 추가", StringComparison.Ordinal) &&
                       status.Contains("2/8", StringComparison.Ordinal) &&
                       status.Contains("보라", StringComparison.Ordinal) &&
                       !status.Contains("강조 준비 실패", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(15), "second highlight rule");
            AssertRegisteredPhrase(session, "Write");
            AssertRegisteredPhrase(session, "timeout");
            SelectSearch(session, "timeout");
            ToggleHighlight(session);
            session.WaitUntil(() => session.Status().Contains("문구 강조를 해제했습니다", StringComparison.Ordinal) && session.Status().Contains("1/8", StringComparison.Ordinal), TimeSpan.FromSeconds(15), "toggle highlight off");
            Assert.Equal(count, session.Text("CountStatus"));
            Assert.DoesNotContain("강조 준비 실패", session.Status(), StringComparison.Ordinal);
            Assert.False(session.App.HasExited);
        });
        Assert.Equal(hash, Hash(log));
    }

    [Fact]
    public void Scenario10_SurroundingLogKeepsFilterOnReturn()
    {
        string folder = NewFolder("context");
        string log = Path.Combine(folder, "context.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, ContextLog());
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("context.log");
            ShowOnlyThread(session, "T 9");
            session.WaitUntil(() => session.Text("CountStatus").Contains("표시 3/", StringComparison.Ordinal), TimeSpan.FromSeconds(15), "center record only");
            GoTo(session, "26");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 26줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "context anchor");
            ClickAnalysisItem(session, "현재 줄 주변 로그 보기");
            session.WaitUntil(() =>
            {
                var status = Find(session, "ContextStatus");
                string text = status?.Name ?? "";
                return text.Contains("기준: 원본 26줄", StringComparison.Ordinal) &&
                       text.Contains("앞 20기록 / 뒤 20기록", StringComparison.Ordinal) &&
                       text.Contains("모든 스레드", StringComparison.Ordinal) &&
                       !session.IsBusy();
            }, TimeSpan.FromSeconds(20), "surrounding log");
            Assert.Contains("41/51건", session.Text("CountStatus"), StringComparison.Ordinal);
            Assert.Contains("표시 43/53줄", session.Text("CountStatus"), StringComparison.Ordinal);
            Assert.DoesNotContain("표시 41/", session.Text("CountStatus"), StringComparison.Ordinal);
            Assert.Contains("주변 로그", session.Require("ExportButton").Name ?? "", StringComparison.Ordinal);
            session.Require("ReturnContextButton").AsButton().Invoke();
            session.WaitUntil(
                () => Find(session, "ContextStatus") is null &&
                      session.Text("CountStatus").Contains("표시 3/", StringComparison.Ordinal) &&
                      session.Text("PositionStatus").Contains("원본 26줄", StringComparison.Ordinal) &&
                      !session.IsBusy(),
                TimeSpan.FromSeconds(20),
                "return from surrounding log");
            Assert.Contains("필터 결과", session.Require("ExportButton").Name ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Scenario11_FilterKeepsOrMovesPosition()
    {
        string folder = NewFolder("anchor");
        string log = Path.Combine(folder, "anchor.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8,
            SyntheticLog.Line(1, 1, "alpha-keep stays") +
            SyntheticLog.Line(2, 2, "beta-hide gone") +
            SyntheticLog.Line(3, 1, "alpha-keep other"));
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("anchor.log");
            GoTo(session, "1");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 1줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "start on line 1");
            ApplyInclude(session, SyntheticLog.KeepToken);
            session.WaitUntil(
                () => session.Text("CountStatus").Contains("표시 2/", StringComparison.Ordinal) &&
                      session.Text("PositionStatus").Contains("원본 1줄", StringComparison.Ordinal) &&
                      session.Status().Contains("필터 적용 완료", StringComparison.Ordinal) &&
                      !session.Status().Contains("가까운 원본 줄", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15),
                "kept line stayed");
            ClickNamedButton(session, "내용 필터 해제");
            session.WaitUntil(() => session.Text("CountStatus").Contains("표시 3/", StringComparison.Ordinal) && !session.IsBusy(), TimeSpan.FromSeconds(15), "content filter cleared");
            GoTo(session, "2");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 2줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "hidden candidate");
            ApplyInclude(session, SyntheticLog.KeepToken);
            session.WaitUntil(
                () => session.Text("PositionStatus").Contains("원본 3줄", StringComparison.Ordinal) &&
                      session.Status().Contains("가까운 원본 줄로 이동", StringComparison.Ordinal) &&
                      session.Text("CountStatus").Contains("표시 2/", StringComparison.Ordinal),
                TimeSpan.FromSeconds(15),
                "hidden line moved forward on a tie");
            ClickNamedButton(session, "전체 해제");
            session.WaitUntil(
                () => session.Text("CountStatus").Contains("표시 0/", StringComparison.Ordinal) &&
                      session.Text("PositionStatus").Contains("원본 줄 없음", StringComparison.Ordinal) &&
                      !session.IsBusy(),
                TimeSpan.FromSeconds(15),
                "no threads selected");
            ClickNamedButton(session, "전체 선택");
            session.WaitUntil(
                () => session.Text("PositionStatus").Contains("원본 3줄", StringComparison.Ordinal) &&
                      session.Text("CountStatus").Contains("표시 2/", StringComparison.Ordinal) &&
                      !session.IsBusy(),
                TimeSpan.FromSeconds(15),
                "anchor restored after the empty view");
        });
    }

    [Fact]
    public void Scenario12_SettingsPersistAfterRestart()
    {
        string folder = NewFolder("settings");
        string log = Path.Combine(folder, "settings.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8,
            SyntheticLog.Line(1, 1, "height-token one") +
            SyntheticLog.Line(2, 1, "height-token two"));
        string settingsDir = Path.Combine(Path.GetDirectoryName(app.ExePath)!, "user-data");
        string settingsFile = Path.Combine(settingsDir, "ui-settings.json");
        DeleteSettings(app.ExePath);
        int grownWidth = 0;
        int grownHeight = 0;
        try
        {
            UiSta.Run(() =>
            {
                using var session = UiSession.Launch(app.ExePath, log, app.ScreenshotDirectory, nameof(Scenario12_SettingsPersistAfterRestart));
                try
                {
                    session.WaitForOpen("settings.log");
                    ClickMenuPath(session, "보기", "화면 테마", "라이트");
                    ClickMenuPath(session, "보기", "글자 크기", "20");
                    ClickMenuPath(session, "보기", "행 간격", "좁은 간격");
                    ClickMenuPath(session, "보기", "자동 줄바꿈");
                    Assert.True(MenuChecked(session, "보기", "화면 테마", "라이트"), session.Describe());
                    Assert.True(MenuChecked(session, "보기", "글자 크기", "20"), session.Describe());
                    Assert.True(MenuChecked(session, "보기", "행 간격", "좁은 간격"), session.Describe());
                    Assert.True(MenuChecked(session, "보기", "자동 줄바꿈"), session.Describe());
                    int widthBefore = Width(session, "ThreadList");
                    DragSplitter(session, "PanelSplitter", 80);
                    session.WaitUntil(() => Width(session, "ThreadList") >= widthBefore + 30, TimeSpan.FromSeconds(10), "panel width did not grow");
                    grownWidth = Width(session, "ThreadList");
                    session.ShowSearch();
                    session.SetSearchQuery("height-token");
                    session.WaitForSearchStatus(status => status.Contains('개'), "results for the height token");
                    session.WaitUntil(() => TryHeight(session, "ResultsList", out int height) && height > 40, TimeSpan.FromSeconds(10), "results list");
                    int heightBefore = Height(session, "ResultsList");
                    var splitter = session.Window.FindFirstDescendant(cf => cf.ByName("검색 결과 높이 조절"))
                        ?? throw new Xunit.Sdk.XunitException("Results splitter was not found. " + session.Describe());
                    var splitterRect = splitter.BoundingRectangle;
                    Mouse.Click(new System.Drawing.Point(splitterRect.X + Math.Max(1, splitterRect.Width / 2), splitterRect.Y + Math.Max(1, splitterRect.Height / 2)));
                    Wait.UntilInputIsProcessed();
                    for (int i = 0; i < 4; i++)
                    {
                        Keyboard.Type(VirtualKeyShort.UP);
                        Wait.UntilInputIsProcessed();
                        Thread.Sleep(60);
                    }
                    session.WaitUntil(() => TryHeight(session, "ResultsList", out int height) && height >= heightBefore + 20, TimeSpan.FromSeconds(10), "results height did not grow");
                    grownHeight = Height(session, "ResultsList");
                    ClickNamed(session, "검색 결과 목록 접기");
                    session.WaitUntil(() => FindNamed(session, "검색 결과 목록 펼치기") is not null, TimeSpan.FromSeconds(10), "results did not collapse");
                    Thread.Sleep(800);
                }
                catch (Exception ex)
                {
                    session.CaptureFailure(ex);
                    throw;
                }
            }, TimeSpan.FromMinutes(3));
            string saved = WaitForSettings(settingsFile);
            Assert.Contains("\"Dark\": false", saved, StringComparison.Ordinal);
            Assert.Contains("\"FontSize\": 20", saved, StringComparison.Ordinal);
            Assert.Contains("\"Compact\": true", saved, StringComparison.Ordinal);
            Assert.Contains("\"WordWrap\": true", saved, StringComparison.Ordinal);
            Assert.Contains("\"ResultsCollapsed\": true", saved, StringComparison.Ordinal);
            Assert.DoesNotContain("height-token", saved, StringComparison.Ordinal);
            UiSta.Run(() =>
            {
                using var session = UiSession.Launch(app.ExePath, log, app.ScreenshotDirectory, nameof(Scenario12_SettingsPersistAfterRestart) + "_restart");
                try
                {
                    session.WaitForOpen("settings.log");
                    Assert.True(MenuChecked(session, "보기", "화면 테마", "라이트"), "theme was not restored. " + saved);
                    Assert.True(MenuChecked(session, "보기", "글자 크기", "20"), "font was not restored. " + saved);
                    Assert.True(MenuChecked(session, "보기", "행 간격", "좁은 간격"), "density was not restored. " + saved);
                    Assert.True(MenuChecked(session, "보기", "자동 줄바꿈"), "wrap was not restored. " + saved);
                    int width = Width(session, "ThreadList");
                    Assert.InRange(width, grownWidth - 24, grownWidth + 24);
                    session.ShowSearch();
                    Assert.True(string.IsNullOrEmpty(session.Require("SearchBox").AsTextBox().Text), session.Describe());
                    session.SetSearchQuery("height-token");
                    session.WaitForSearchStatus(status => status.Contains('개'), "search after restart");
                    session.WaitUntil(() => FindNamed(session, "검색 결과 목록 펼치기") is not null, TimeSpan.FromSeconds(10), "collapsed results were not restored");
                    ClickNamed(session, "검색 결과 목록 펼치기");
                    session.WaitUntil(() => TryHeight(session, "ResultsList", out int height) && Math.Abs(height - grownHeight) <= 24, TimeSpan.FromSeconds(10), "results height was not restored");
                    Assert.Contains("settings.log", session.Text("FileLabel"), StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    session.CaptureFailure(ex);
                    throw;
                }
            }, TimeSpan.FromMinutes(3));
        }
        finally
        {
            DeleteSettings(app.ExePath);
        }
    }

    [Fact]
    public void Scenario13_TimeDifferenceBetweenRecords()
    {
        string folder = NewFolder("time");
        string log = Path.Combine(folder, "analysis.log");
        SyntheticLog.Write(log, LogEncodingKind.Utf8, AnalysisLog);
        Run(log, TimeSpan.FromMinutes(3), session =>
        {
            session.WaitForOpen("analysis.log");
            GoTo(session, "1");
            ClickAnalysisItem(session, "현재 줄을 시간 비교 기준으로 지정");
            session.WaitUntil(() => (Find(session, "TimeSummary")?.Name ?? "").Contains("기준: 원본 1줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "time anchor");
            GoTo(session, "4");
            ClickAnalysisItem(session, "기준과 현재 줄 시간 차이");
            session.WaitUntil(() =>
            {
                string summary = Find(session, "TimeSummary")?.Name ?? "";
                return summary.Contains("원본 4줄", StringComparison.Ordinal) && summary.Contains("+00:00:00.012", StringComparison.Ordinal) && summary.Contains("12 ms", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10), "+12 ms");
            ClickNamedButton(session, "시간 비교 해제");
            session.WaitUntil(() => Find(session, "TimeSummary") is null, TimeSpan.FromSeconds(10), "time result cleared");
            GoTo(session, "4");
            ClickAnalysisItem(session, "현재 줄을 시간 비교 기준으로 지정");
            session.WaitUntil(() => (Find(session, "TimeSummary")?.Name ?? "").Contains("기준: 원본 4줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "reversed anchor");
            GoTo(session, "1");
            ClickAnalysisItem(session, "기준과 현재 줄 시간 차이");
            session.WaitUntil(() =>
            {
                string summary = Find(session, "TimeSummary")?.Name ?? "";
                return summary.Contains("\u221200:00:00.012", StringComparison.Ordinal) && summary.Contains("-12 ms", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10), "minus 12 ms");
            ClickNamedButton(session, "시간 비교 해제");
            session.WaitUntil(() => Find(session, "TimeSummary") is null, TimeSpan.FromSeconds(10), "time result cleared again");
            GoTo(session, "1");
            ClickAnalysisItem(session, "현재 줄을 시간 비교 기준으로 지정");
            GoTo(session, "2");
            ClickAnalysisItem(session, "기준과 현재 줄 시간 차이");
            session.WaitUntil(() =>
            {
                string summary = Find(session, "TimeSummary")?.Name ?? "";
                return summary.Contains("원본 2줄", StringComparison.Ordinal) && summary.Contains("+00:00:00.000", StringComparison.Ordinal) && summary.Contains(" 0 ms", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(10), "same record is zero");
            GoTo(session, "7");
            Assert.False(AnalysisItemEnabled(session, "기준과 현재 줄 시간 차이"), "invalid timestamp must disable compare");
            Assert.False(AnalysisItemEnabled(session, "현재 줄을 시간 비교 기준으로 지정"), "invalid timestamp must disable anchor");
            Assert.Contains("+00:00:00.000", Find(session, "TimeSummary")?.Name ?? "", StringComparison.Ordinal);
            Assert.Contains(" 0 ms", Find(session, "TimeSummary")?.Name ?? "", StringComparison.Ordinal);
        });
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

    private static void ShowOnlyThread(UiSession session, string label)
    {
        var list = session.Require("ThreadList");
        AutomationElement? box = null;
        session.WaitUntil(
            () => (box = list.FindFirstDescendant(cf => cf.ByControlType(ControlType.CheckBox).And(cf.ByName(label)))) is not null,
            TimeSpan.FromSeconds(10),
            "thread checkbox " + label);
        if (box is null)
            throw new Xunit.Sdk.XunitException("Thread checkbox was not found: " + label + " " + session.Describe());
        AutomationElement? solo = null;
        var current = box;
        for (int depth = 0; depth < 6 && current is not null && solo is null; depth++)
        {
            solo = current.FindAllChildren().FirstOrDefault(child => child.ControlType == ControlType.Button && child.Name == "단독");
            current = current.Parent;
        }
        if (solo is null)
            throw new Xunit.Sdk.XunitException("Solo button was not found for " + label + " " + session.Describe());
        solo.AsButton().Invoke();
        session.WaitUntil(() => session.Status().Contains("필터 적용 완료", StringComparison.Ordinal) && !session.IsBusy(), TimeSpan.FromSeconds(20), "thread filter");
    }

    private static void GoTo(UiSession session, string line)
    {
        session.FocusWindow();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
        Wait.UntilInputIsProcessed();
        var box = session.Require("GoToBox").AsTextBox();
        var stable = System.Diagnostics.Stopwatch.StartNew();
        var limit = System.Diagnostics.Stopwatch.StartNew();
        while (limit.Elapsed < TimeSpan.FromSeconds(5))
        {
            if ((box.Text ?? "") != line)
            {
                box.Text = line;
                stable.Restart();
            }
            else if (stable.Elapsed >= TimeSpan.FromMilliseconds(300))
                break;
            Thread.Sleep(40);
        }
        session.WaitUntil(() => (box.Text ?? "") == line, TimeSpan.FromSeconds(5), "goto text " + line);
        session.Require("GoToSubmitButton").AsButton().Invoke();
    }

    private static void ApplyInclude(UiSession session, string phrase)
    {
        var expander = session.Require("ContentFilterExpander");
        if (expander.Patterns.ExpandCollapse.TryGetPattern(out var expand))
            expand.Expand();
        else
            expander.Click();
        var include = session.Require("IncludeBox").AsTextBox();
        include.Text = phrase;
        var apply = session.Require("ApplyFilterButton").AsButton();
        session.WaitUntil(() => apply.IsEnabled, TimeSpan.FromSeconds(10), "filter button stayed disabled");
        apply.Invoke();
    }

    internal static void SelectSearch(UiSession session, string query)
    {
        session.ShowSearch();
        session.SetSearchQuery(query);
        session.WaitForSearchStatus(status => status.Contains('개') && !status.Contains("결과 없음", StringComparison.Ordinal), "hits for " + query);
        PressSearch(session, shift: false);
        session.WaitForSearchStatus(status => status.Contains(" / ", StringComparison.Ordinal), "selected " + query);
    }

    internal static void ToggleHighlight(UiSession session)
    {
        session.FocusWindow();
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F8);
        Wait.UntilInputIsProcessed();
    }

    internal static void AssertRegisteredPhrase(UiSession session, string phrase)
    {
        Window? dialog = null;
        for (int attempt = 1; dialog is null && attempt <= 3; attempt++)
        {
            if (attempt > 1)
            {
                session.FocusWindow();
                session.Press(VirtualKeyShort.ESCAPE);
                Wait.UntilInputIsProcessed();
                Thread.Sleep(200);
            }
            session.WaitUntil(() => !session.IsBusy(), TimeSpan.FromSeconds(10), "viewer stayed busy before the highlight manager");
            // A missed mouse click leaves the manager closed. Later attempts use the invoke pattern.
            if (attempt == 1)
                ClickAnalysisItem(session, "강조 문구 관리");
            else
                InvokeAnalysisItem(session, "강조 문구 관리");
            try
            {
                session.WaitUntil(() => (dialog = HighlightDialog(session)) is not null, TimeSpan.FromSeconds(8), "highlight manager");
            }
            catch (Xunit.Sdk.XunitException) when (attempt < 3)
            {
            }
        }
        if (dialog is null)
            throw new Xunit.Sdk.XunitException("highlight manager " + session.Describe());
        var edits = dialog.FindAllDescendants(cf => cf.ByName("등록된 강조 문구 편집"));
        Assert.Contains(edits, edit => string.Equals(edit.AsTextBox().Text, phrase, StringComparison.Ordinal));
        var cancel = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("취소")))
            ?? throw new Xunit.Sdk.XunitException("Highlight cancel was not found.");
        cancel.AsButton().Invoke();
        session.WaitUntil(() => HighlightDialog(session) is null, TimeSpan.FromSeconds(10), "highlight manager stayed open");
    }

    private static void ClickAnalysisItem(UiSession session, string header) =>
        ActivateAnalysisItem(session, header, invoke: false);

    private static void InvokeAnalysisItem(UiSession session, string header) =>
        ActivateAnalysisItem(session, header, invoke: true);

    private static void ActivateAnalysisItem(UiSession session, string header, bool invoke)
    {
        var item = OpenAnalysisItem(session, header);
        if (!item.IsEnabled)
            throw new Xunit.Sdk.XunitException("Analysis item is disabled: " + header + " " + session.Describe());
        if (invoke && item.Patterns.Invoke.TryGetPattern(out var pattern))
            pattern.Invoke();
        else
            item.Click();
        Wait.UntilInputIsProcessed();
    }

    private static bool AnalysisItemEnabled(UiSession session, string header)
    {
        var item = OpenAnalysisItem(session, header);
        bool enabled = item.IsEnabled;
        session.Press(VirtualKeyShort.ESCAPE);
        return enabled;
    }

    private static AutomationElement OpenAnalysisItem(UiSession session, string header)
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
        Wait.UntilInputIsProcessed();
        AutomationElement? target = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (target is null && watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            target = menu.Items.FirstOrDefault(item => (item.Name ?? "").Contains(header, StringComparison.Ordinal));
            target ??= FindNamed(session, header);
            if (target is null)
                Thread.Sleep(50);
        }
        return target ?? throw new Xunit.Sdk.XunitException("Analysis item was not found: " + header + " " + string.Join(" | ", menu.Items.Select(item => item.Name)));
    }

    private static void ClickMenuPath(UiSession session, params string[] path)
    {
        var leaf = OpenMenuPath(session, path);
        leaf.Click();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
    }

    private static bool MenuChecked(UiSession session, params string[] path)
    {
        var leaf = OpenMenuPath(session, path);
        bool check = leaf.Patterns.Toggle.TryGetPattern(out var toggle)
            ? toggle.ToggleState == ToggleState.On
            : leaf.AsMenuItem().IsChecked == true;
        session.Press(VirtualKeyShort.ESCAPE);
        session.Press(VirtualKeyShort.ESCAPE);
        return check;
    }

    private static AutomationElement OpenMenuPath(UiSession session, string[] path)
    {
        session.FocusWindow();
        var current = FindTopMenu(session, path[0]);
        current.AsMenuItem().Expand();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(80);
        for (int i = 1; i < path.Length; i++)
        {
            var next = current.AsMenuItem().Items.FirstOrDefault(item => (item.Name ?? "").StartsWith(path[i], StringComparison.Ordinal))
                ?? FindNamed(session, path[i])
                ?? throw new Xunit.Sdk.XunitException("Menu item was not found: " + path[i] + " under " + current.Name);
            if (i < path.Length - 1)
            {
                next.AsMenuItem().Expand();
                Wait.UntilInputIsProcessed();
                Thread.Sleep(80);
            }
            current = next;
        }
        return current;
    }

    private static AutomationElement FindTopMenu(UiSession session, string prefix)
    {
        var menus = session.Window.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
        return menus.FirstOrDefault(item =>
        {
            string name = item.Name ?? "";
            return name.Equals(prefix, StringComparison.Ordinal) || name.StartsWith(prefix + "(", StringComparison.Ordinal);
        }) ?? throw new Xunit.Sdk.XunitException("Top menu was not found: " + prefix + " " + session.Describe());
    }

    private static void ClickNamed(UiSession session, string name)
    {
        var element = FindNamed(session, name) ?? throw new Xunit.Sdk.XunitException("Control was not found: " + name + " " + session.Describe());
        element.Click();
        Wait.UntilInputIsProcessed();
    }

    private static void ClickNamedButton(UiSession session, string name)
    {
        var element = session.Window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
            ?? throw new Xunit.Sdk.XunitException("Button was not found: " + name + " " + session.Describe());
        element.AsButton().Invoke();
    }

    private static void DragSplitter(UiSession session, string automationId, int deltaX)
    {
        var splitter = session.Require(automationId);
        var rect = splitter.BoundingRectangle;
        var start = new System.Drawing.Point(rect.X + Math.Max(1, rect.Width / 2), rect.Y + Math.Max(1, rect.Height / 2));
        Mouse.MoveTo(start);
        Wait.UntilInputIsProcessed();
        Mouse.Down(MouseButton.Left);
        for (int step = 1; step <= 4; step++)
        {
            Mouse.MoveTo(new System.Drawing.Point(start.X + deltaX * step / 4, start.Y));
            Thread.Sleep(40);
        }
        Mouse.Up(MouseButton.Left);
        Wait.UntilInputIsProcessed();
    }

    private static int Width(UiSession session, string automationId)
    {
        var rect = session.Require(automationId).BoundingRectangle;
        return rect.Width;
    }

    private static int Height(UiSession session, string automationId)
    {
        if (!TryHeight(session, automationId, out int height))
            throw new Xunit.Sdk.XunitException("Height was not available for " + automationId + " " + session.Describe());
        return height;
    }

    private static bool TryHeight(UiSession session, string automationId, out int height)
    {
        height = 0;
        var element = Find(session, automationId);
        if (element is null)
            return false;
        try
        {
            int value = element.BoundingRectangle.Height;
            if (value <= 0)
                return false;
            height = value;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    private static AutomationElement? Find(UiSession session, string automationId)
    {
        try { return session.Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return null; }
    }

    private static AutomationElement? FindNamed(UiSession session, string name)
    {
        try
        {
            var found = session.Window.FindFirstDescendant(cf => cf.ByName(name));
            if (found is not null)
                return found;
            foreach (var window in session.App.GetAllTopLevelWindows(session.Automation))
            {
                found = window.FindFirstDescendant(cf => cf.ByName(name));
                if (found is not null)
                    return found;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
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
        try { windows.AddRange(session.Window.ModalWindows); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        try
        {
            windows.AddRange(session.Window.FindAllChildren(cf => cf.ByControlType(ControlType.Window)).Select(element => element.AsWindow()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        return windows;
    }

    private static IEnumerable<Window> TopLevelWindows(UiSession session)
    {
        try { return session.App.GetAllTopLevelWindows(session.Automation); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { return []; }
    }

    private static void PressSearch(UiSession session, bool shift)
    {
        session.FocusWindow();
        var box = session.Require("SearchBox");
        box.Focus();
        box.Click();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(80);
        if (shift)
            Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.RETURN);
        else
            Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }

    private static string MappingLog()
    {
        var text = new StringBuilder();
        for (int line = 1; line <= 110; line++)
        {
            int thread = line is 100 or 105 ? 3 : 7;
            string marker = line == 100 ? " A한🙂" : "";
            text.Append($"[000:00:01.000] [T {thread}] synthetic original row {line}{marker} (synthetic.cpp:900)\n");
        }
        return text.ToString();
    }

    private static string ContextLog()
    {
        var text = new StringBuilder();
        for (int i = 1; i <= 25; i++)
            text.Append(SyntheticLog.Line(i, 1, "neighbor-before " + i));
        text.Append("[000:00:26.000] [T 9] center-anchor\n");
        text.Append("  center-body-one\n");
        text.Append("  center-body-two\n");
        for (int i = 1; i <= 25; i++)
            text.Append(SyntheticLog.Line(i, 2, "neighbor-after " + i));
        return text.ToString();
    }

    private static string WaitForSettings(string path)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                if (text.Contains("\"Dark\": false", StringComparison.Ordinal))
                    return text;
            }
            Thread.Sleep(100);
        }
        return File.Exists(path) ? File.ReadAllText(path) : "<missing " + path + ">";
    }

    private static void DeleteSettings(string exePath)
    {
        string dir = Path.Combine(Path.GetDirectoryName(exePath)!, "user-data");
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string NewFolder(string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "tlv-ui-" + name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Hash(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
