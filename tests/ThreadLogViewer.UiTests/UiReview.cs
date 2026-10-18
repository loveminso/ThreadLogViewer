using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace ThreadLogViewer.UiTests;

internal static class UiReview
{
    private const string ReviewVariable = "THREADLOGVIEWER_UI_REVIEW";
    private const string ScaleVariable = "THREADLOGVIEWER_UI_REVIEW_SCALE";
    private static readonly (int Width, int Height)[] Sizes = [(1024, 768), (1920, 1080)];

    internal static void Run(string publishedExe, string outputDirectory)
    {
        string exe = Path.GetFullPath(publishedExe);
        if (!File.Exists(exe))
            throw new FileNotFoundException("Published exe was not found.", exe);
        string output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        foreach (string file in Directory.GetFiles(output))
            File.Delete(file);
        Environment.SetEnvironmentVariable("THREADLOGVIEWER_EXE", exe);
        string displayNote = UiReviewCapture.TryRaiseResolution(1920, 1080) ?? "";
        Console.WriteLine(displayNote);
        var shots = new List<UiReviewShot>();
        var findings = new List<UiReviewFinding>();
        string folder = Path.Combine(Path.GetTempPath(), "tlv-ui-review-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var app = new PublishedApp();
            Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "review.log");
            SyntheticLog.Write(log, LogEncodingKind.Utf8, ReviewLog());
            foreach (int scale in new[] { 100, 150 })
            {
                var lint = new UiReviewLint(scale == 150 ? 1.5 : 1.0);
                try
                {
                    foreach (string theme in new[] { "dark", "light" })
                        CaptureTheme(app, log, output, scale, theme, lint, shots);
                }
                finally
                {
                    findings.AddRange(lint.Findings);
                    lint.DisposeFont();
                }
            }
        }
        finally
        {
            UiReviewReport.Write(output, displayNote, shots, findings);
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        if (shots.Count != 72)
            throw new InvalidOperationException($"Expected 72 screenshots but wrote {shots.Count}.");
        foreach (var shot in shots)
        {
            string path = Path.Combine(output, shot.File);
            if (!File.Exists(path))
                throw new InvalidOperationException("Screenshot is missing: " + shot.File);
        }
        Console.WriteLine($"UI review wrote {shots.Count} screenshots and {findings.Count} lint warnings.");
    }

    private static void CaptureTheme(PublishedApp app, string log, string output, int scale, string theme, UiReviewLint lint, List<UiReviewShot> shots)
    {
        Console.WriteLine($"=== UI review scale {scale}% theme {theme} ===");
        var environment = new Dictionary<string, string>
        {
            [ReviewVariable] = "1",
            [ScaleVariable] = scale == 150 ? "150" : "100"
        };
        UiSession? session = null;
        try
        {
            session = UiSession.Launch(app.ExePath, null, output, "ui-review-failure", environment);
            session.WaitUntil(
                () => (session.Window.Title ?? "").Contains("ThreadLog Viewer", StringComparison.Ordinal) && !session.IsBusy(),
                TimeSpan.FromSeconds(30),
                "startup");
            session.Require("FileLabel");
            SetTheme(session, theme);
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Shoot(session, lint, output, shots, "empty-start", "빈 시작 화면", theme, scale, size.Width, size.Height, null, steady: true);
            }
            session.DropFile(log);
            session.WaitForOpen("review.log");
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Shoot(session, lint, output, shots, "opened-log", "연 로그", theme, scale, size.Width, size.Height, null, steady: true);
            }
            session.ShowSearch();
            session.SetSearchQuery("timeout");
            session.WaitForSearchStatus(status => status.Contains('개') && !status.Contains("결과 없음", StringComparison.Ordinal), "search hits");
            PressEnter(session);
            session.WaitForSearchStatus(status => status.Contains(" / ", StringComparison.Ordinal), "search selection");
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Shoot(session, lint, output, shots, "search-results", "검색 결과", theme, scale, size.Width, size.Height, null, steady: true);
            }
            CloseSearch(session);
            session.ApplyIncludeFilter("timeout");
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Shoot(session, lint, output, shots, "active-filter", "적용된 필터", theme, scale, size.Width, size.Height, null, steady: true);
            }
            GoTo(session, "9");
            session.WaitUntil(() => session.Text("PositionStatus").Contains("원본 9줄", StringComparison.Ordinal), TimeSpan.FromSeconds(10), "context line");
            session.Press(VirtualKeyShort.ESCAPE);
            InvokeMenu(session, "분석", "현재 줄 주변 로그 보기");
            session.WaitUntil(() =>
            {
                string text = Find(session, "ContextStatus")?.Name ?? "";
                return text.Contains("주변 로그", StringComparison.Ordinal) && !session.IsBusy();
            }, TimeSpan.FromSeconds(20), "surrounding log");
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Shoot(session, lint, output, shots, "surrounding-log", "주변 로그", theme, scale, size.Width, size.Height, null, steady: true);
            }
            session.Require("ReturnContextButton").AsButton().Invoke();
            session.WaitUntil(() =>
            {
                var status = Find(session, "ContextStatus");
                return (status is null || status.IsOffscreen) && !session.IsBusy();
            }, TimeSpan.FromSeconds(20), "return from surrounding log");
            session.ShowSearch();
            session.SetSearchQuery("timeout");
            session.WaitForSearchStatus(status => status.Contains('개') && !status.Contains("결과 없음", StringComparison.Ordinal), "highlight search");
            PressEnter(session);
            session.WaitForSearchStatus(status => status.Contains(" / ", StringComparison.Ordinal), "highlight selection");
            session.FocusWindow();
            Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F8);
            Wait.UntilInputIsProcessed();
            session.WaitUntil(() =>
            {
                string status = session.Status();
                return status.Contains("문구 강조 추가", StringComparison.Ordinal) &&
                       !status.Contains("강조 준비 실패", StringComparison.Ordinal);
            }, TimeSpan.FromSeconds(15), "highlight rule");
            CloseSearch(session);
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Window dialog = OpenHighlight(session);
                Shoot(session, lint, output, shots, "highlight-manager", "강조 문구 관리", theme, scale, size.Width, size.Height, dialog, steady: false);
                CloseHighlight(dialog);
            }
            AddBadRule(session);
            session.WaitUntil(() =>
            {
                string status = session.Status();
                return status.Contains("강조 준비 실패", StringComparison.Ordinal) &&
                       !status.Contains("강조 준비 중", StringComparison.Ordinal) &&
                       !session.IsBusy();
            }, TimeSpan.FromSeconds(15), "highlight failure notice");
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                Shoot(session, lint, output, shots, "failure-notice", "강조 준비 실패 안내", theme, scale, size.Width, size.Height, null, steady: true);
            }
            foreach (var size in Sizes)
            {
                Resize(session, size.Width, size.Height);
                OpenFileMenu(session);
                Shoot(session, lint, output, shots, "menus", "파일 메뉴", theme, scale, size.Width, size.Height, null, steady: false);
                session.Press(VirtualKeyShort.ESCAPE);
                session.Press(VirtualKeyShort.F1);
                Window about = WaitForAbout(session);
                Shoot(session, lint, output, shots, "about", "단축키 도움말", theme, scale, size.Width, size.Height, about, steady: false);
                CloseAbout(about);
                session.WaitUntil(() => FindAbout(session) is null, TimeSpan.FromSeconds(8), "help dialog stayed open");
            }
        }
        catch (Exception ex)
        {
            session?.CaptureFailure(ex);
            throw;
        }
        finally
        {
            session?.Dispose();
        }
    }

    private static void Shoot(UiSession session, UiReviewLint lint, string output, List<UiReviewShot> shots, string state, string label, string theme, int scale, int width, int height, Window? dialog, bool steady)
    {
        if (steady)
            Steady(session);
        Thread.Sleep(250);
        string file = $"{state}__{theme}__{width}x{height}__scale-{scale}.png";
        string path = Path.Combine(output, file);
        bool blank = true;
        for (int attempt = 1; attempt <= 3 && blank; attempt++)
        {
            UiReviewCapture.Save(session.App.ProcessId, path);
            blank = UiReviewCapture.LooksBlank(path);
            if (blank)
                Thread.Sleep(300);
        }
        if (blank || new FileInfo(path).Length < 1000)
            throw new InvalidOperationException("Screenshot was blank: " + file);
        var roots = new List<AutomationElement> { session.Window };
        AutomationElement tabRoot = session.Window;
        if (dialog is not null)
        {
            roots.Add(dialog);
            tabRoot = dialog;
        }
        lint.Inspect(session.Automation, roots, tabRoot, state, theme, width + "x" + height, scale);
        shots.Add(new UiReviewShot(file, state, label, theme, width, height, scale, lint.LastTabOrder.ToArray()));
        Console.WriteLine("captured " + file);
    }

    private static void Resize(UiSession session, int width, int height)
    {
        var hwnd = session.Window.Properties.NativeWindowHandle.Value;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            UiReviewCapture.MoveWindow(hwnd, width, height);
            Thread.Sleep(150);
            var actual = UiReviewCapture.WindowSize(hwnd);
            if (Math.Abs(actual.Width - width) <= 24 && Math.Abs(actual.Height - height) <= 24)
            {
                Console.WriteLine($"window {actual.Width}x{actual.Height} target {width}x{height}");
                return;
            }
        }
        var last = UiReviewCapture.WindowSize(hwnd);
        throw new InvalidOperationException($"Window stayed {last.Width}x{last.Height} instead of {width}x{height}.");
    }

    private static void SetTheme(UiSession session, string theme)
    {
        string item = theme == "light" ? "라이트" : "다크";
        InvokeMenu(session, "보기", "화면 테마", item);
        if (!MenuIsChecked(session, item))
            throw new InvalidOperationException("Theme was not applied: " + item + " " + session.Describe());
    }

    private static bool MenuIsChecked(UiSession session, string item)
    {
        session.FocusWindow();
        var view = TopMenu(session, "보기");
        view.AsMenuItem().Expand();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(80);
        var group = view.AsMenuItem().Items.FirstOrDefault(menu => (menu.Name ?? "").StartsWith("화면 테마", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Theme menu was not found. " + session.Describe());
        group.AsMenuItem().Expand();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(80);
        var leaf = group.AsMenuItem().Items.FirstOrDefault(menu => (menu.Name ?? "").StartsWith(item, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Theme item was not found: " + item + " " + session.Describe());
        bool check = leaf.Patterns.Toggle.TryGetPattern(out var toggle)
            ? toggle.ToggleState == ToggleState.On
            : leaf.AsMenuItem().IsChecked == true;
        session.Press(VirtualKeyShort.ESCAPE);
        session.Press(VirtualKeyShort.ESCAPE);
        return check;
    }

    private static void OpenFileMenu(UiSession session)
    {
        session.FocusWindow();
        var file = TopMenu(session, "파일");
        file.AsMenuItem().Expand();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
    }

    private static void InvokeMenu(UiSession session, params string[] path)
    {
        session.FocusWindow();
        var current = TopMenu(session, path[0]);
        current.AsMenuItem().Expand();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(80);
        for (int i = 1; i < path.Length; i++)
        {
            AutomationElement? next = null;
            var watch = Stopwatch.StartNew();
            while (next is null && watch.Elapsed < TimeSpan.FromSeconds(5))
            {
                next = current.AsMenuItem().Items.FirstOrDefault(item => (item.Name ?? "").StartsWith(path[i], StringComparison.Ordinal));
                if (next is null)
                    Thread.Sleep(40);
            }
            if (next is null)
                throw new InvalidOperationException("Menu item was not found: " + path[i] + " " + session.Describe());
            if (i < path.Length - 1)
            {
                next.AsMenuItem().Expand();
                Wait.UntilInputIsProcessed();
                Thread.Sleep(80);
                current = next;
                continue;
            }
            next.Click();
            Wait.UntilInputIsProcessed();
        }
    }

    private static AutomationElement TopMenu(UiSession session, string prefix)
    {
        var menus = session.Window.FindAllDescendants(condition => condition.ByControlType(ControlType.MenuItem));
        return menus.FirstOrDefault(item =>
        {
            string name = item.Name ?? "";
            return name.Equals(prefix, StringComparison.Ordinal) || name.StartsWith(prefix + "(", StringComparison.Ordinal);
        }) ?? throw new InvalidOperationException("Top menu was not found: " + prefix + " " + session.Describe());
    }

    private static void PressEnter(UiSession session)
    {
        var box = session.Require("SearchBox");
        box.Focus();
        try { box.Click(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        Wait.UntilInputIsProcessed();
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
    }

    private static void CloseSearch(UiSession session)
    {
        var close = session.Window.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName("검색 닫기")));
        try { close?.AsButton().Invoke(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        session.WaitUntil(() =>
        {
            var box = Find(session, "SearchBox");
            return box is null || box.IsOffscreen;
        }, TimeSpan.FromSeconds(8), "search stayed open");
    }

    private static void GoTo(UiSession session, string line)
    {
        session.FocusWindow();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_G);
        Wait.UntilInputIsProcessed();
        var box = session.Require("GoToBox").AsTextBox();
        var stable = Stopwatch.StartNew();
        var limit = Stopwatch.StartNew();
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

    private static Window OpenHighlight(UiSession session)
    {
        Window? dialog = null;
        for (int attempt = 1; dialog is null && attempt <= 2; attempt++)
        {
            if (attempt > 1)
            {
                session.FocusWindow();
                session.Press(VirtualKeyShort.ESCAPE);
                Thread.Sleep(200);
            }
            InvokeMenu(session, "분석", "강조 문구 관리");
            var watch = Stopwatch.StartNew();
            while (dialog is null && watch.Elapsed < TimeSpan.FromSeconds(8))
            {
                dialog = FindHighlight(session);
                if (dialog is null)
                    Thread.Sleep(50);
            }
        }
        return dialog ?? throw new InvalidOperationException("Highlight manager did not open. " + session.Describe());
    }

    private static void CloseHighlight(Window dialog)
    {
        var cancel = dialog.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName("취소")));
        try { cancel?.AsButton().Invoke(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            try
            {
                if (!dialog.Properties.NativeWindowHandle.IsSupported)
                    return;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                return;
            }
            try
            {
                dialog.Focus();
                Keyboard.Type(VirtualKeyShort.ESCAPE);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                return;
            }
            Thread.Sleep(80);
        }
    }

    private static void AddBadRule(UiSession session)
    {
        Window dialog = OpenHighlight(session);
        var box = dialog.FindFirstDescendant(condition => condition.ByAutomationId("HighlightPendingBox"))?.AsTextBox()
            ?? throw new InvalidOperationException("Highlight phrase box was not found. " + session.Describe());
        box.Text = "broken\uD800";
        session.WaitUntil(() => (box.Text ?? "").Contains('\uD800'), TimeSpan.FromSeconds(5), "bad highlight phrase");
        var add = dialog.FindFirstDescendant(condition => condition.ByAutomationId("HighlightAddButton"))?.AsButton()
            ?? throw new InvalidOperationException("Highlight add button was not found.");
        session.WaitUntil(() => add.IsEnabled, TimeSpan.FromSeconds(5), "highlight add button stayed disabled");
        add.Invoke();
        session.WaitUntil(
            () => dialog.FindAllDescendants().Any(element => (element.Name ?? "").Contains("2/8", StringComparison.Ordinal)),
            TimeSpan.FromSeconds(5),
            "bad highlight rule was not registered");
        var apply = dialog.FindFirstDescendant(condition => condition.ByAutomationId("HighlightApplyButton"))?.AsButton()
            ?? throw new InvalidOperationException("Highlight apply button was not found.");
        apply.Invoke();
    }

    private static Window? FindHighlight(UiSession session) =>
        FindWindow(session, window =>
            string.Equals(window.AutomationId, "HighlightManagerWindow", StringComparison.Ordinal) ||
            (window.Title ?? "").Contains("강조 문구 관리", StringComparison.Ordinal));

    private static Window WaitForAbout(UiSession session)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(8))
        {
            var about = FindAbout(session);
            if (about is not null)
                return about;
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("Help dialog did not open. " + session.Describe());
    }

    private static Window? FindAbout(UiSession session) =>
        FindWindow(session, window => SafeTitle(window).Contains("단축키", StringComparison.Ordinal));

    private static Window? FindWindow(UiSession session, Func<Window, bool> match)
    {
        foreach (var window in RelatedWindows(session))
        {
            try
            {
                if (match(window))
                    return window;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        }
        return null;
    }

    private static IEnumerable<Window> RelatedWindows(UiSession session)
    {
        var windows = new List<Window>();
        try { windows.AddRange(session.Window.ModalWindows); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        try
        {
            windows.AddRange(session.Window.FindAllChildren(condition => condition.ByControlType(ControlType.Window)).Select(element => element.AsWindow()));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        try { windows.AddRange(session.App.GetAllTopLevelWindows(session.Automation)); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        return windows;
    }

    private static string SafeTitle(Window window)
    {
        try { return window.Title ?? ""; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return ""; }
    }

    private static void CloseAbout(Window dialog)
    {
        var ok = dialog.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName("확인")))
            ?? dialog.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByName("OK")))
            ?? dialog.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByAutomationId("1")))
            ?? dialog.FindFirstDescendant(condition => condition.ByControlType(ControlType.Button).And(condition.ByAutomationId("2")));
        try
        {
            if (ok is not null)
                ok.AsButton().Invoke();
            else
            {
                dialog.Focus();
                Keyboard.Type(VirtualKeyShort.RETURN);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            try
            {
                dialog.Focus();
                Keyboard.Type(VirtualKeyShort.RETURN);
            }
            catch (Exception second) when (second is COMException or InvalidOperationException)
            {
            }
        }
    }

    private static void Steady(UiSession session)
    {
        try
        {
            var editor = session.Window.FindFirstDescendant(condition => condition.ByAutomationId("LogEditor"));
            if (editor is not null && !editor.IsOffscreen)
            {
                editor.Focus();
                return;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }
        session.FocusWindow();
    }

    private static AutomationElement? Find(UiSession session, string automationId)
    {
        try { return session.Window.FindFirstDescendant(condition => condition.ByAutomationId(automationId)); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { return null; }
    }

    private static string ReviewLog()
    {
        var text = new StringBuilder();
        for (int i = 1; i <= 8; i++)
            text.Append(SyntheticLog.Line(i, 1, "neighbor-before " + i));
        text.Append(SyntheticLog.Line(9, 2, "timeout checksum center-anchor marker-zeta"));
        text.Append("  center-body synthetic\n");
        for (int i = 10; i <= 16; i++)
            text.Append(SyntheticLog.Line(i, 3, "neighbor-after " + i));
        return text.ToString();
    }
}
