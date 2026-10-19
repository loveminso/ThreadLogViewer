using System.IO;
using System.Text.RegularExpressions;

namespace ThreadLogViewer.UiTests;

internal static class UiReviewPaths
{
    private static readonly Regex ReviewFolderPrefix = new(
        @"(?i)[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s""]+[\\/]+AppData[\\/]+Local[\\/]+Temp[\\/]+tlv-ui-review-[0-9a-f]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UserAppDataPrefix = new(
        @"(?i)[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s""]+[\\/]+AppData\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex UserProfilePrefix = new(
        @"(?i)[A-Za-z]:[\\/]+Users[\\/]+[^\\/\s""]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RunnerCheckoutPrefix = new(
        @"(?i)[A-Za-z]:[\\/]+a[\\/]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static readonly string[] RunnerPathFiles = ["ui-lint.json", "ui-lint.md", "index.md", "index.html"];

    internal static string RedactReviewPaths(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        string text = ReviewFolderPrefix.Replace(value, "<temp>");
        text = UserAppDataPrefix.Replace(text, "<temp>");
        text = UserProfilePrefix.Replace(text, "<temp>");
        return RunnerCheckoutPrefix.Replace(text, match => match.Value.Contains('\\') ? "<temp>\\" : "<temp>/");
    }

    internal static void RejectRunnerPaths(string output)
    {
        foreach (string name in RunnerPathFiles)
        {
            string text = File.ReadAllText(Path.Combine(output, name));
            if (ContainsRunnerPath(text))
                throw new InvalidOperationException(name + " still contains a runner user path.");
        }
    }

    internal static bool ContainsRunnerPath(string text) =>
        text.Contains("runneradmin", StringComparison.OrdinalIgnoreCase)
        || text.Contains("AppData", StringComparison.OrdinalIgnoreCase)
        || text.Contains(@":\a\", StringComparison.OrdinalIgnoreCase)
        || text.Contains(":/a/", StringComparison.OrdinalIgnoreCase);
}

internal static class UiReviewMenuPopup
{
    internal static bool IsPopup(bool isMenuOrMenuItem, bool barFound, bool isBar, bool parentKnown, bool parentIsBar)
    {
        if (!isMenuOrMenuItem || !barFound || isBar || !parentKnown)
            return false;
        return !parentIsBar;
    }
}
