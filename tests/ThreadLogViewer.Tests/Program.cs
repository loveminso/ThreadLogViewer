using System.Diagnostics;
using System.Text.Json;
using ICSharpCode.AvalonEdit.Document;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.Tests;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--workbench-benchmark")
            return await WorkbenchPerformanceProbe.RunAsync(args[1], int.Parse(args[2]));
        if (args.Length == 5 && args[0] == "--benchmark" && args[3] == "--memory-refreshes")
            return MemoryBenchmark.Run(args[1], int.Parse(args[2]), int.Parse(args[4]));
        if (args.Length != 3 || args[0] != "--benchmark")
        {
            Console.WriteLine("Automated tests: dotnet test. Benchmark: --benchmark <output-directory> <MiB> [--memory-refreshes <count>]");
            return 1;
        }
        string folder = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(folder);
        int mib = int.Parse(args[2]);
        string path = Path.Combine(folder, $"synthetic-{mib}MiB.log");
        SyntheticBenchmarkLog.Write(path, mib);
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
            Entries = log.Entries.Count, Lines = log.Lines.Count, FilteredEntries = filtered.EntryCount,
            FilteredLines = filtered.Count, OpenReadDecodeParseMs = openMs,
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
}
