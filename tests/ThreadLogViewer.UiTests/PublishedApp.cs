namespace ThreadLogViewer.UiTests;

public sealed class PublishedApp : IDisposable
{
    private readonly string copyRoot;
    public string ExePath { get; }
    public string ScreenshotDirectory { get; }

    public PublishedApp()
    {
        string sourceExe = LocatePublishedExe();
        string sourceRoot = Path.GetDirectoryName(sourceExe)!;
        if (!File.Exists(Path.Combine(sourceRoot, "coreclr.dll")))
            throw new InvalidOperationException(
                "UI tests require the self-contained publish folder (coreclr.dll next to ThreadLogViewer.exe). " +
                "Run scripts/publish.ps1 and set THREADLOGVIEWER_EXE to that exe.");
        copyRoot = Path.Combine(Path.GetTempPath(), "tlv-ui-app-" + Guid.NewGuid().ToString("N"));
        CopyTree(sourceRoot, copyRoot);
        ExePath = Path.Combine(copyRoot, "ThreadLogViewer.exe");
        ScreenshotDirectory = LocateScreenshotDirectory();
        Directory.CreateDirectory(ScreenshotDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(copyRoot))
                Directory.Delete(copyRoot, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string LocatePublishedExe()
    {
        string? env = Environment.GetEnvironmentVariable("THREADLOGVIEWER_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env))
            return Path.GetFullPath(env);
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                string candidate = Path.Combine(dir.FullName, "artifacts", "ThreadLogViewer-win-x64", "ThreadLogViewer.exe");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }
        throw new InvalidOperationException(
            "Published exe was not found. Set THREADLOGVIEWER_EXE to artifacts/ThreadLogViewer-win-x64/ThreadLogViewer.exe.");
    }

    private static string LocateScreenshotDirectory()
    {
        string? env = Environment.GetEnvironmentVariable("UI_SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(env))
            return Path.GetFullPath(env);
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ThreadLogViewer.slnx")))
                    return Path.Combine(dir.FullName, "TestResults", "ui-screenshots");
                dir = dir.Parent;
            }
        }
        return Path.Combine(Path.GetTempPath(), "tlv-ui-screenshots");
    }

    private static void CopyTree(string source, string destination)
    {
        var root = new DirectoryInfo(source);
        if (root.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new IOException("Publish folder must not be a link.");
        Directory.CreateDirectory(destination);
        foreach (var directory in root.EnumerateDirectories())
        {
            if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            if (directory.Name.Equals("user-data", StringComparison.OrdinalIgnoreCase))
                continue;
            CopyTree(directory.FullName, Path.Combine(destination, directory.Name));
        }
        foreach (var file in root.EnumerateFiles())
        {
            if (file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                continue;
            file.CopyTo(Path.Combine(destination, file.Name), overwrite: false);
        }
    }
}
