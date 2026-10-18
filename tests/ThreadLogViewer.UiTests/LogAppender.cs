using System.Diagnostics;
using System.Text;

namespace ThreadLogViewer.UiTests;

/// <summary>
/// Separate PowerShell process that holds the log open for append and writes complete lines.
/// Pause, idle, stop, and ready are named manual-reset events created by that process.
/// The viewer never sees this process's handle; both sides use read/write sharing.
/// </summary>
internal sealed class LogAppender : IDisposable
{
    private readonly string path;
    private readonly Process process;
    private readonly EventWaitHandle readyEvent;
    private readonly EventWaitHandle stopEvent;
    private readonly EventWaitHandle pauseEvent;
    private readonly EventWaitHandle idleEvent;
    private readonly object outputGate;
    private readonly StringBuilder output;

    private LogAppender(
        string path,
        Process process,
        EventWaitHandle readyEvent,
        EventWaitHandle stopEvent,
        EventWaitHandle pauseEvent,
        EventWaitHandle idleEvent,
        object outputGate,
        StringBuilder output)
    {
        this.path = path;
        this.process = process;
        this.readyEvent = readyEvent;
        this.stopEvent = stopEvent;
        this.pauseEvent = pauseEvent;
        this.idleEvent = idleEvent;
        this.outputGate = outputGate;
        this.output = output;
    }

    public static LogAppender Start(string logPath, LogEncodingKind kind, bool startPaused)
    {
        string script = Path.Combine(AppContext.BaseDirectory, "AppendHelper.ps1");
        if (!File.Exists(script))
            throw new FileNotFoundException("Append helper script was not copied to the test output.", script);
        string token = @"Local\tlv-append-" + Guid.NewGuid().ToString("N");
        string readyName = token + "-ready";
        string stopName = token + "-stop";
        string pauseName = token + "-pause";
        string idleName = token + "-idle";
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
        start.ArgumentList.Add("-ReadyName");
        start.ArgumentList.Add(readyName);
        start.ArgumentList.Add("-StopName");
        start.ArgumentList.Add(stopName);
        start.ArgumentList.Add("-PauseName");
        start.ArgumentList.Add(pauseName);
        start.ArgumentList.Add("-IdleName");
        start.ArgumentList.Add(idleName);
        start.ArgumentList.Add("-PrefixB64");
        start.ArgumentList.Add(prefix);
        start.ArgumentList.Add("-SuffixB64");
        start.ArgumentList.Add(suffix);
        if (startPaused)
            start.ArgumentList.Add("-StartPaused");
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var gate = new object();
        var captured = new StringBuilder();
        EventWaitHandle? ready = null;
        EventWaitHandle? stop = null;
        EventWaitHandle? pause = null;
        EventWaitHandle? idle = null;
        try
        {
            process.OutputDataReceived += (_, e) => AppendCaptured(gate, captured, e.Data);
            process.ErrorDataReceived += (_, e) => AppendCaptured(gate, captured, e.Data);
            if (!process.Start())
                throw new InvalidOperationException("Could not start the append helper.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            ready = OpenSignal(readyName, process, gate, captured, TimeSpan.FromSeconds(45));
            stop = OpenSignal(stopName, process, gate, captured, TimeSpan.FromSeconds(45));
            pause = OpenSignal(pauseName, process, gate, captured, TimeSpan.FromSeconds(45));
            idle = OpenSignal(idleName, process, gate, captured, TimeSpan.FromSeconds(45));
            var appender = new LogAppender(logPath, process, ready, stop, pause, idle, gate, captured);
            appender.WaitUntilSignaled(ready, TimeSpan.FromSeconds(45), "Append helper did not open the log.");
            return appender;
        }
        catch
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            ready?.Dispose();
            stop?.Dispose();
            pause?.Dispose();
            idle?.Dispose();
            process.Dispose();
            throw;
        }
    }

    public long Length => new FileInfo(path).Length;

    private bool EndsWithNewline()
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return false;
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() == '\n';
    }

    public void Pause()
    {
        // Drop a stale idle signal so this wait means the helper observed this pause.
        idleEvent.Reset();
        pauseEvent.Set();
        WaitUntilSignaled(idleEvent, TimeSpan.FromSeconds(10), "Append helper did not become idle.");
    }

    public void Resume()
    {
        pauseEvent.Reset();
        idleEvent.Reset();
    }

    public void WaitForGrowth(long previousLength, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (Length > previousLength && EndsWithNewline())
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
            stopEvent.Set();
            pauseEvent.Reset();
            if (!process.WaitForExit(10_000))
                process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
            readyEvent.Dispose();
            stopEvent.Dispose();
            pauseEvent.Dispose();
            idleEvent.Dispose();
        }
    }

    private void WaitUntilSignaled(EventWaitHandle handle, TimeSpan timeout, string message)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (handle.WaitOne(50))
                return;
            if (process.HasExited)
                throw new InvalidOperationException(message + " " + OutputText());
        }
        throw new TimeoutException(message + " " + OutputText());
    }

    private static EventWaitHandle OpenSignal(string name, Process process, object gate, StringBuilder captured, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (EventWaitHandle.TryOpenExisting(name, out EventWaitHandle? opened) && opened is not null)
                return opened;
            if (process.HasExited)
                throw new InvalidOperationException("Append helper exited before signal " + name + ". " + ReadCaptured(gate, captured));
            Thread.Sleep(30);
        }
        throw new TimeoutException("Append helper did not create signal " + name + ". " + ReadCaptured(gate, captured));
    }

    private static void AppendCaptured(object gate, StringBuilder captured, string? line)
    {
        if (line is null)
            return;
        lock (gate)
            captured.AppendLine(line);
    }

    private string OutputText() => ReadCaptured(outputGate, output);

    private static string ReadCaptured(object gate, StringBuilder captured)
    {
        lock (gate)
            return captured.ToString();
    }
}
