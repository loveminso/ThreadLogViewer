using System.IO;
using System.Reflection;
using System.Text;
using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class WindowTitleTests
{
    private const string EmptyTitlePattern = "^ThreadLog Viewer v[0-9]+\\.[0-9]+\\.[0-9]+\\z";
    private const string OpenTitlePattern = "^.+ \u2014 ThreadLog Viewer v[0-9]+\\.[0-9]+\\.[0-9]+\\z";

    [Fact]
    public void TitlePatternsUseAsciiDigitsAndTheAbsoluteEnd()
    {
        Assert.Equal("^ThreadLog Viewer v[0-9]+\\.[0-9]+\\.[0-9]+\\z", EmptyTitlePattern);
        Assert.Equal("^.+ \u2014 ThreadLog Viewer v[0-9]+\\.[0-9]+\\.[0-9]+\\z", OpenTitlePattern);
        Assert.Matches(EmptyTitlePattern, "ThreadLog Viewer v0.8.3");
        Assert.DoesNotMatch(EmptyTitlePattern, "ThreadLog Viewer v0.8.3\n");
        Assert.DoesNotMatch(EmptyTitlePattern, "ThreadLog Viewer v\u0660.8.3");
        Assert.Matches(OpenTitlePattern, "opened-first.log \u2014 ThreadLog Viewer v0.8.3");
        Assert.DoesNotMatch(OpenTitlePattern, "opened-first.log \u2014 ThreadLog Viewer v0.8.3\n");
    }

    [Fact]
    public void FormatUsesMajorMinorBuildAndDropsRevision()
    {
        Assert.Equal("ThreadLog Viewer v0.8.3", WindowTitle.Format(new Version(0, 8, 3, 0)));
        Assert.Equal("ThreadLog Viewer v0.8.3", WindowTitle.Format(new Version(0, 8, 3, 9)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowTitle.Format(new Version(0, 8)));
    }

    [Fact]
    public void FromReadsAssemblyVersionWithoutInformationalSuffix()
    {
        Assembly assembly = typeof(WindowTitle).Assembly;
        Version? version = assembly.GetName().Version;
        Assert.NotNull(version);
        string title = WindowTitle.From(assembly);
        string propsVersion = PropsVersion();
        Assert.Equal(WindowTitle.Format(version!), title);
        Assert.Equal("ThreadLog Viewer v" + propsVersion, title);
        Assert.Matches(EmptyTitlePattern, title);
        Assert.DoesNotContain("+", title, StringComparison.Ordinal);
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is not null)
            Assert.Equal(propsVersion, informational.Split('+')[0]);
    }

    [Fact]
    public void WithSessionKeepsTheEmDashSeparator()
    {
        string appTitle = WindowTitle.Format(new Version(0, 8, 3, 0));
        string withFile = WindowTitle.WithSession("opened-first.log", appTitle);
        Assert.Equal("opened-first.log \u2014 " + appTitle, withFile);
        Assert.Matches(OpenTitlePattern, withFile);
    }

    [Fact]
    public Task MainWindowTitleMatchesTodayForEmptyAndOpenFile() => InSta(async () =>
    {
        using var folder = new SyntheticFolder();
        string path = await folder.Write("opened-first.log", "[000:00:01] [T1] synthetic title\r\n");
        var window = new MainWindow(folder.Path, false);
        try
        {
            string expected = "ThreadLog Viewer v" + PropsVersion();
            Assert.Equal(expected, window.Title);
            Assert.Matches(EmptyTitlePattern, window.Title);
            var open = typeof(MainWindow).GetMethod("OpenFilesAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(open);
            bool opened = await (Task<bool>)open!.Invoke(window, [new[] { path }])!;
            Assert.True(opened);
            Assert.Equal("opened-first.log \u2014 " + expected, window.Title);
            Assert.Matches(OpenTitlePattern, window.Title);
        }
        finally { window.Close(); }
    });

    private static string PropsVersion()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string props = Path.Combine(dir.FullName, "Directory.Build.props");
            if (File.Exists(props) && File.Exists(Path.Combine(dir.FullName, "ThreadLogViewer.slnx")))
            {
                foreach (string line in File.ReadAllLines(props))
                {
                    const string open = "<Version>";
                    const string close = "</Version>";
                    int start = line.IndexOf(open, StringComparison.Ordinal);
                    int end = line.IndexOf(close, StringComparison.Ordinal);
                    if (start >= 0 && end > start)
                        return line.Substring(start + open.Length, end - start - open.Length).Trim();
                }
                throw new InvalidOperationException("Directory.Build.props has no Version.");
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Directory.Build.props was not found.");
    }

    private static Task InSta(Func<Task> action) => (Task)typeof(WindowFeatureTests)
        .GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;

    private sealed class SyntheticFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-window-title", Guid.NewGuid().ToString("N"));
        public SyntheticFolder() => Directory.CreateDirectory(Path);
        public async Task<string> Write(string name, string text)
        {
            string path = System.IO.Path.Combine(Path, name);
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false, true));
            return path;
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
