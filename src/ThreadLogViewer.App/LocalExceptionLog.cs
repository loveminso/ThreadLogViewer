using System.IO;
using System.Text;

namespace ThreadLogViewer.App;

/// <summary>
/// Optional local log under %LOCALAPPDATA%\ThreadLogViewer.
/// Never writes the opened log, and never writes beside it.
/// </summary>
public static class LocalExceptionLog
{
    public static void TryWrite(string exceptionType, string channel)
    {
        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData)) return;
            localAppData = Path.GetFullPath(localAppData);
            string root = Path.Combine(localAppData, "ThreadLogViewer");
            string path = Path.GetFullPath(Path.Combine(root, "unhandled-exceptions.log"));
            if (!path.StartsWith(localAppData + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(root);
            string line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}\t{channel}\t{exceptionType}{Environment.NewLine}";
            File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch
        {
            // Logging is optional and must not escape or touch a source log.
        }
    }
}
