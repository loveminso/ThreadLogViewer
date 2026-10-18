using System.Text;
using System.Text.Json;

namespace ThreadLogViewer.UiTests;

internal sealed record UiReviewShot(string File, string State, string StateLabel, string Theme, int Width, int Height, int Scale, string[] TabOrder);

internal static class UiReviewReport
{
    internal const string ScaleLimitation =
        "CI does not change the Windows session DPI, which stays at 100%. " +
        "100% captures use outer window pixels as DIPs. " +
        "150% captures set THREADLOGVIEWER_UI_REVIEW=1 and THREADLOGVIEWER_UI_REVIEW_SCALE=150 on the viewer process only. " +
        "That process applies a 1.5 LayoutTransform to the window content, skips the monitor work-area clamp, and uses software rendering so PrintWindow is not black. " +
        "The outer window stays the requested pixel size, so layout DIPs are about that size divided by 1.5. " +
        "This matches a fixed-pixel window on a 150% monitor. It does not exercise Per-Monitor V2 font hinting, and the native title bar stays at the session DPI. " +
        "Menu popups, the highlight-manager window, and the help window are separate windows and are not enlarged by the content transform. " +
        "When the variables are unset, layout, render mode, and the monitor clamp are unchanged.";

    internal const string ThemeNote =
        "The app has dark and light themes. Both are captured. No theme was added for this review.";

    internal const string FailureNote =
        "The failure notice is the status-bar text that starts with 강조 준비 실패 after one broken highlight rule. " +
        "The modal exception MessageBox is shown only for an unhandled UI exception. The review does not inject one.";

    internal static void Write(string directory, string displayNote, IReadOnlyList<UiReviewShot> shots, IReadOnlyList<UiReviewFinding> findings, IReadOnlyList<UiReviewBucket> buckets, UiReviewFilterCounts filtered)
    {
        Directory.CreateDirectory(directory);
        int truncated = findings.Count(item => item.Category == UiReviewLint.TruncatedText);
        int outside = findings.Count(item => item.Category == UiReviewLint.OutsideBounds);
        int tab = findings.Count(item => item.Category == UiReviewLint.TabOrder);
        int missing = findings.Count(item => item.Category == UiReviewLint.MissingName);
        int stored = findings.Count;
        int dropped = buckets.Sum(bucket => bucket.Dropped);
        bool capped = dropped > 0;
        var scales = buckets
            .GroupBy(bucket => bucket.Scale)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                int scaleStored = group.Sum(bucket => bucket.Stored);
                int scaleDropped = group.Sum(bucket => bucket.Dropped);
                return new { scale = group.Key, stored = scaleStored, dropped = scaleDropped, capped = scaleDropped > 0 };
            })
            .ToArray();
        var summary = new
        {
            truncatedText = truncated,
            outsideBounds = outside,
            tabOrder = tab,
            missingName = missing,
            stored,
            dropped,
            total = stored,
            filtered = new
            {
                systemMenuBar = filtered.SystemMenuBar,
                menuPopup = filtered.MenuPopup,
                menuHeader = filtered.MenuHeader,
                total = filtered.Total
            }
        };
        var payload = new
        {
            warningsFailTheBuild = false,
            screenshotCount = shots.Count,
            findingCap = UiReviewLint.FindingCap,
            capped,
            scaleLimitation = ScaleLimitation,
            themes = ThemeNote,
            failureNotice = FailureNote,
            display = displayNote,
            summary,
            scales,
            buckets = buckets.Select(bucket => new
            {
                state = bucket.State,
                theme = bucket.Theme,
                scale = bucket.Scale,
                stored = bucket.Stored,
                dropped = bucket.Dropped,
                capped = bucket.Capped
            }),
            screenshots = shots.Select(shot => new
            {
                file = shot.File,
                state = shot.State,
                stateLabel = shot.StateLabel,
                theme = shot.Theme,
                width = shot.Width,
                height = shot.Height,
                scale = shot.Scale,
                tabOrder = shot.TabOrder
            }),
            findings = findings.Select(item => new
            {
                category = item.Category,
                state = item.State,
                theme = item.Theme,
                size = item.Size,
                scale = item.Scale,
                detail = item.Detail
            })
        };
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, "ui-lint.json"), JsonSerializer.Serialize(payload, jsonOptions), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(Path.Combine(directory, "index.md"), IndexMarkdown(displayNote, shots), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "index.html"), IndexHtml(shots), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, "ui-lint.md"), LintMarkdown(truncated, outside, tab, missing, stored, dropped, capped, filtered, scales.Select(item => (item.scale, item.stored, item.dropped, item.capped)).ToArray(), buckets, findings), new UTF8Encoding(false));
    }

    private static string IndexMarkdown(string displayNote, IReadOnlyList<UiReviewShot> shots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# UI review screenshots");
        builder.AppendLine();
        builder.AppendLine("Synthetic logs only. These pictures are evidence. Lint warnings are in `ui-lint.md` and do not by themselves mean a failed build.");
        builder.AppendLine();
        builder.AppendLine(ThemeNote);
        builder.AppendLine();
        builder.AppendLine(FailureNote);
        builder.AppendLine();
        builder.AppendLine("## DPI");
        builder.AppendLine();
        builder.AppendLine(ScaleLimitation);
        builder.AppendLine();
        builder.AppendLine(displayNote);
        builder.AppendLine();
        builder.AppendLine("| File | State | Theme | Size | Scale |");
        builder.AppendLine("| --- | --- | --- | --- | ---: |");
        foreach (var shot in shots)
            builder.AppendLine($"| {shot.File} | {shot.StateLabel} (`{shot.State}`) | {shot.Theme} | {shot.Width}x{shot.Height} | {shot.Scale}% |");
        builder.AppendLine();
        builder.AppendLine($"Screenshot count: {shots.Count}.");
        return builder.ToString();
    }

    private static string IndexHtml(IReadOnlyList<UiReviewShot> shots)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ko\"><head><meta charset=\"utf-8\"><title>UI review screenshots</title>");
        builder.AppendLine("<style>body{font-family:Segoe UI,sans-serif;margin:24px;background:#111;color:#eee}img{max-width:960px;width:100%;border:1px solid #333;background:#000}section{margin:0 0 28px}</style>");
        builder.AppendLine("</head><body><h1>UI review screenshots</h1>");
        builder.AppendLine("<p>Warnings in ui-lint.md do not fail the build. Synthetic data only.</p><ul>");
        foreach (var shot in shots)
            builder.AppendLine($"<li><a href=\"#{shot.File}\">{shot.File}</a> — {shot.StateLabel}, {shot.Theme}, {shot.Width}x{shot.Height}, {shot.Scale}%</li>");
        builder.AppendLine("</ul>");
        foreach (var shot in shots)
        {
            builder.AppendLine($"<section id=\"{shot.File}\"><h2>{shot.StateLabel}</h2><p>{shot.File}<br>{shot.Theme} · {shot.Width}x{shot.Height} · {shot.Scale}%</p>");
            builder.AppendLine($"<img alt=\"{shot.State} {shot.Theme} {shot.Width}x{shot.Height} {shot.Scale}%\" src=\"{shot.File}\"></section>");
        }
        builder.AppendLine("</body></html>");
        return builder.ToString();
    }

    private static string LintMarkdown(int truncated, int outside, int tab, int missing, int stored, int dropped, bool capped, UiReviewFilterCounts filtered, (int Scale, int Stored, int Dropped, bool Capped)[] scales, IReadOnlyList<UiReviewBucket> buckets, IReadOnlyList<UiReviewFinding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# UI lint");
        builder.AppendLine();
        builder.AppendLine("These are warnings. They do not fail the build. Only a crash of the review step fails the job.");
        builder.AppendLine();
        builder.AppendLine("Truncation is estimated from the UIA tree. UIA does not expose WPF DesiredSize. A single-line text element is reported when its name, measured with Segoe UI at the window font size times the review scale, is wider than its bounding width and the element is stretched to its parent or the excess is large. An ellipsis in the accessible name is also reported. A filter is counted only when it suppresses a finding that would otherwise be stored. The System menu bar is omitted from outside-bounds. Menu and MenuItem elements are omitted from outside-bounds only when their top-level window is not the main window, which is a popup. The main window menu bar stays in that check. Text whose parent is a MenuItem is measured again after `(_X)` and `_` are removed, and a real overflow is still stored. Other ellipsis, including filter headers and search results, stays. Menu items are omitted from the unreachable-Tab check because they open with Alt and arrows. List items are omitted because the list is one tab stop.");
        builder.AppendLine();
        builder.AppendLine($"Each state, theme, and scale stores at most {UiReviewLint.FindingCap} warnings. Both window sizes share that bucket. Category counts below are stored warnings only.");
        builder.AppendLine();
        builder.AppendLine("## Summary");
        builder.AppendLine();
        builder.AppendLine("| Category | Stored |");
        builder.AppendLine("| --- | ---: |");
        builder.AppendLine($"| truncated-text | {truncated} |");
        builder.AppendLine($"| outside-bounds | {outside} |");
        builder.AppendLine($"| tab-order | {tab} |");
        builder.AppendLine($"| missing-name | {missing} |");
        builder.AppendLine($"| stored | {stored} |");
        builder.AppendLine($"| dropped | {dropped} |");
        builder.AppendLine();
        builder.AppendLine("Filtered counts are findings a filter suppressed instead of storing.");
        builder.AppendLine();
        builder.AppendLine("| Filter | Suppressed |");
        builder.AppendLine("| --- | ---: |");
        builder.AppendLine($"| system menu bar | {filtered.SystemMenuBar} |");
        builder.AppendLine($"| menu popup | {filtered.MenuPopup} |");
        builder.AppendLine($"| menu header | {filtered.MenuHeader} |");
        builder.AppendLine($"| filtered total | {filtered.Total} |");
        builder.AppendLine();
        builder.AppendLine("| Scale | Stored | Dropped |");
        builder.AppendLine("| --- | ---: | ---: |");
        foreach (var scale in scales)
            builder.AppendLine($"| {scale.Scale}% | {scale.Stored} | {scale.Dropped} |");
        builder.AppendLine();
        if (scales.Length == 0)
            builder.AppendLine("No scale buckets.");
        foreach (var scale in scales)
        {
            string line = $"{scale.Scale}%: {scale.Stored}건 저장 / {scale.Dropped}건 생략";
            if (scale.Capped)
                line += ", 실제 개수는 저장분보다 많음";
            builder.AppendLine(line);
        }
        builder.AppendLine();
        if (capped)
            builder.AppendLine("At least one bucket reached the cap. The stored total is not the full warning count.");
        builder.AppendLine();
        builder.AppendLine("## Buckets");
        builder.AppendLine();
        builder.AppendLine("| State | Theme | Scale | Stored | Dropped |");
        builder.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var bucket in buckets)
            builder.AppendLine($"| {bucket.State} | {bucket.Theme} | {bucket.Scale}% | {bucket.Stored} | {bucket.Dropped} |");
        builder.AppendLine();
        if (findings.Count == 0)
        {
            builder.AppendLine("No warnings.");
            return builder.ToString();
        }
        builder.AppendLine("## Warnings");
        builder.AppendLine();
        foreach (var group in findings.GroupBy(item => item.State + "|" + item.Theme + "|" + item.Size + "|" + item.Scale))
        {
            var first = group.First();
            builder.AppendLine($"### {first.State} / {first.Theme} / {first.Size} / {first.Scale}%");
            builder.AppendLine();
            foreach (var item in group)
                builder.AppendLine($"- `{item.Category}` {item.Detail}");
            builder.AppendLine();
        }
        return builder.ToString();
    }
}
