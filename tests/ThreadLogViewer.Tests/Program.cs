using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using ICSharpCode.AvalonEdit.Document;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.Tests;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 3 || args[0] != "--benchmark")
        {
            Console.WriteLine("Automated tests: dotnet test. Benchmark: --benchmark <output-directory> <MiB>");
            return 1;
        }
        string folder = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(folder);
        int mib = int.Parse(args[2]);
        string path = Path.Combine(folder, $"synthetic-{mib}MiB.log");
        Generate(path, mib);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        long initialWorkingSet = process.WorkingSet64;
        var watch = Stopwatch.StartNew();
        var log = await Task.Run(() => LogFileReader.ReadAsync(path));
        double openMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var all = LogProjection.Create(log, log.Threads.Select(t => t.ThreadId));
        var document = new TextDocument(all.Text);
        double allDocumentMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var filtered = LogProjection.Create(log, [3, 7, null]);
        double filterMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var filteredDocument = new TextDocument(filtered.Text);
        double filteredDocumentMs = watch.Elapsed.TotalMilliseconds;
        process.Refresh();
        var report = new
        {
            Timestamp = DateTimeOffset.Now, Environment.OSVersion, Runtime = Environment.Version.ToString(),
            ProcessorCount = Environment.ProcessorCount, SyntheticFile = path, Bytes = new FileInfo(path).Length,
            Lines = log.Lines.Count, FilteredLines = filtered.Count, OpenReadDecodeParseMs = openMs,
            AllProjectionAndAvalonDocumentMs = allDocumentMs, FilterProjectionMs = filterMs,
            FilteredAvalonDocumentMs = filteredDocumentMs, InitialWorkingSetBytes = initialWorkingSet,
            WorkingSetBytes = process.WorkingSet64, PeakWorkingSetBytes = process.PeakWorkingSet64,
            ManagedHeapBytes = GC.GetTotalMemory(false),
            Scope = "Console process: core + AvalonEdit TextDocument creation; excludes WPF layout/render, one cold process run, synthetic data only. Both documents retained."
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(folder, $"benchmark-{mib}MiB.json"), json);
        Console.WriteLine(json);
        GC.KeepAlive(document); GC.KeepAlive(filteredDocument);
        return 0;
    }

    private static void Generate(string path, int mib)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
        long total = 0, target = mib * 1024L * 1024;
        for (int i = 0; total < target; i++)
        {
            string line = i % 113 == 0 ? "DE AD BE EF 00 01 02 03 — synthetic dump without thread" : i % 227 == 0 ? "" :
                $"[{(i / 3600) % 24:00}:{(i / 60) % 60:00}:{i % 60:00}] [T {i % 16}] {(i % 7 == 0 ? "합성 읽기 완료" : "Synthetic Write completed")} LBA={i:000000000} status=OK (testcase.cpp:{100 + i % 300})";
            writer.Write(line); writer.Write('\n');
            total += Encoding.UTF8.GetByteCount(line) + 1;
        }
    }
}
