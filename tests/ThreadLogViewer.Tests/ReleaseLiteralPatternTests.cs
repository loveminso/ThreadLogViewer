using System.Text.RegularExpressions;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class ReleaseLiteralPatternTests
{
    [Fact]
    public void VerifyReleaseDoesNotTreatALongerVersionAsTheShorterLiteral()
    {
        string script = File.ReadAllText(RepoFile("scripts/verify-release.ps1"));
        Match pattern = Regex.Match(script, @"\[regex\]::Matches\(\$decoded, '([^']+)'\)");
        Assert.True(pattern.Success, "verify-release.ps1 does not name the embedded title pattern.");
        Assert.EndsWith("(?![.\\d])", pattern.Groups[1].Value, StringComparison.Ordinal);
        var regex = new Regex(pattern.Groups[1].Value);
        const string expected = "ThreadLog Viewer v0.8.3";
        Assert.Equal(expected, regex.Match(expected).Value);
        Assert.False(HasExact(regex, "ThreadLog Viewer v0.8.30", expected));
        Assert.False(HasExact(regex, "ThreadLog Viewer v0.8.3.1", expected));
        Assert.False(HasExact(regex, "0.8.3.1", expected));
        Assert.Equal("ThreadLog Viewer v0.8.30", regex.Match("ThreadLog Viewer v0.8.30").Value);
        Assert.Equal("ThreadLog Viewer v9.9.9", regex.Match("prefix ThreadLog Viewer v9.9.9 suffix").Value);
    }

    private static bool HasExact(Regex regex, string text, string expected)
    {
        foreach (Match match in regex.Matches(text))
        {
            if (match.Value == expected) return true;
        }
        return false;
    }

    private static string RepoFile(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "ThreadLogViewer.slnx")))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(relative + " was not found.");
    }
}
