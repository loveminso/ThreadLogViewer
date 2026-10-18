using System.Diagnostics;
using System.Text;

namespace ThreadLogViewer.UiTests;

/// <summary>
/// Separate PowerShell process that holds the log open for append and writes complete lines.
/// The viewer never sees this process's handle; both sides use read/write sharing.
/// </summary>
internal sealed class LogAppender : IDisposable
{
    private readonly string path;
    private readonly string readyFile;
    private readonly string stopFile;
    private readonly string pauseFile;
    private readonly string idleFile;
    private readonly Process process;
    private readonly object outputGate = new();
    private readonly StringBuilder output = new();

    private LogAppender(string path, string readyFile, string stopFile, string pauseFile, string idleFile, Process process)
    {
        this.path = path;
        this.readyFile = readyFile;
        this.stopFile = stopFile;
        this.pauseFile = pauseFile;
        this.idleFile = idleFile;
        this.process = process;
    }

    public static LogAppender Start(string logPath, LogEncodingKind kind, bool startPaused)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "AppendHelper.ps1");
        if (!File.Exists(script))
            throw new FileNotFoundException("Append helper script was not copied to the test output.", script);
        string token = "tlv-append-" + Guid.NewGuid().ToString("N");
        string ready = Path.Combine(Path.GetTempPath(), token + ".ready");
        string stop = Path.Combine(Path.GetTempPath(), token + ".stop");
        string pause = Path.Combine(Path.GetTempPath(), token + ".pause");
        string idle = Path.Combine(Path.GetTempPath(), token + ".idle");
        if (startPaused)
            File.WriteAllText(pause, "pause");
        var encoding = SyntheticLog.EncodingOf(kind);
        string prefix = Convert.ToBase64String(encoding.GetBytes("[000:00:04.000] [T 1] " + SyntheticLog.AppendMarker));
        string suffix = Convert.ToBase64String(encoding.GetBytes($" 가나다{SyntheticLog.Distinctive} (synthetic.cpp:40)\n"));
        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(script);
        start.ArgumentList.Add("-Path");
        start.ArgumentList.Add(logPath);
        start.ArgumentList.Add("-ReadyFile");
        start.ArgumentList.Add(ready);
        start.ArgumentList.Add("-StopFile");
        start.ArgumentList.Add(stop);
        start.ArgumentList.Add("-PauseFile");
        start.ArgumentList.Add(pause);
        start.ArgumentList.Add("-IdleFile");
        start.ArgumentList.Add(idle);
        start.ArgumentList.Add("-PrefixB64");
        start.ArgumentList.Add(prefix);
        start.ArgumentList.Add("-SuffixB64");
        start.ArgumentList.Add(suffix);
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var appender = new LogAppender(logPath, ready, stop, pause, idle, process);
        process.OutputDataReceived += (_, e) => appender.AppendOutput(e.Data);
        process.ErrorDataReceived += (_, e) => appender.AppendOutput(e.Data);
        if (!process.Start())
            throw new InvalidOperationException("Could not start the append helper.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        appender.WaitForReady();
        return appender;
    }

    public long Length => new FileInfo(path).Length;

    public void Pause()
    {
        File.WriteAllText(pauseFile, "pause");
        WaitUntilIdle();
    }

    public void Resume()
    {
        if (File.Exists(pauseFile))
            File.Delete(pauseFile);
        if (File.Exists(idleFile))
            File.Delete(idleFile);
    }

    public void WaitForGrowth(long previousLength, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (Length > previousLength)
                return;
            if (process.HasExited)
                throw new InvalidOperationException("Append helper exited early. " + OutputText());
            Thread.Sleep(30);
        }
        throw new TimeoutException($"Log did not grow past {previousLength} bytes. helper={OutputText()}");
    }

    public void Dispose()
    {
        try
        {
            File.WriteAllText(stopFile, "stop");
            Resume();
            if (!process.WaitForExit(10_000))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            foreach (string file in new[] { readyFile, stopFile, pauseFile, idleFile })
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    private void WaitForReady()
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (File.Exists(readyFile))
                return;
            if (process.HasExited)
                throw new InvalidOperationException("Append helper exited before it was ready. " + OutputText());
            Thread.Sleep(30);
        }
        throw new TimeoutException("Append helper did not open the log. " + OutputText());
    }

    private void WaitUntilIdle()
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (File.Exists(idleFile))
                return;
            if (process.HasExited)
                throw new InvalidOperationException("Append helper exited while pausing. " + OutputText());
            Thread.Sleep(30);
        }
        throw new TimeoutException("Append helper did not become idle. " + OutputText());
    }

    private void AppendOutput(string? line)
    {
        if (line is null)
            return;
        lock (outputGate)
            output.AppendLine(line);
    }

    private string OutputText()
    {
        lock (outputGate)
            return output.ToString();
    }
}
