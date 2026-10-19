using System.IO;
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
        Assert.Equal("ThreadLog Viewer v[0-9]+(?:\\.[0-9]+)+", pattern.Groups[1].Value);
        var regex = new Regex(pattern.Groups[1].Value);
        const string expected = "ThreadLog Viewer v0.8.3";
        Assert.Equal(expected, regex.Match(expected).Value);
        Assert.False(HasExact(regex, "ThreadLog Viewer v0.8.30", expected));
        Assert.Equal("ThreadLog Viewer v0.8.30", regex.Match("ThreadLog Viewer v0.8.30").Value);
        const string fourPart = "ThreadLog Viewer v0.8.3.1";
        Assert.Equal(fourPart, regex.Match(fourPart).Value);
        Assert.NotEqual(expected, regex.Match(fourPart).Value);
        const string buildZero = "ThreadLog Viewer v0.8.4.0";
        Assert.Equal(buildZero, regex.Match(buildZero).Value);
        Assert.NotEqual(expected, regex.Match(buildZero).Value);
        const string trailingDot = "ThreadLog Viewer v0.8.4.";
        Assert.Equal("ThreadLog Viewer v0.8.4", regex.Match(trailingDot).Value);
        Assert.NotEqual(expected, regex.Match(trailingDot).Value);
        Assert.False(HasExact(regex, "0.8.3.1", expected));
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
