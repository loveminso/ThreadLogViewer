using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using ICSharpCode.AvalonEdit.Document;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.Tests;

public static class MemoryBenchmark
{
    public const string HighlightPhrase = "status=OK";
    public const string Layers =
        "Included: LogFileReader.ReadAsync for open and each F5; the LogData that read returns; LogProjection.Create for every thread on open; LogProjection.Create for threads 3, 7, and null (the existing timing benchmark's filter, EntryFilter empty) after open and on each F5; SourceLineRemap.Create on each F5; CancellableDocumentFactory.Create, the AvalonEdit TextDocument copy PrepareDocument uses; LogSearch.Find and HighlightIndex for the phrase status=OK, capped at LogSearch.MaxHighlights, the same search RefreshKeywordsAsync runs. Excluded: the MainWindow, WPF layout and render, the dispatcher, FlaUI, and the published ThreadLogViewer.exe process. One process per size. Synthetic UTF-8 only. The generated file is not written again after creation.";
    public const string Gc =
        "GC.Collect, WaitForPendingFinalizers, and GC.Collect run on a thread-pool thread, then GC.GetTotalMemory(false), GC.GetGCMemoryInfo (TotalCommittedBytes, FragmentedBytes, and large-object-heap GenerationInfo[3].SizeAfterBytes), and the process counters are read. The same collection sequence is in WorkbenchPerformanceProbe. Counters are taken while the phase's LogData, projection, document, and highlight stay rooted.";

    private static readonly int?[] FilteredThreads = [3, 7, null];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Run(string folder, int mib, int refreshes)
    {
        if (mib <= 0 || refreshes <= 0)
        {
            Console.Error.WriteLine("MiB and refresh count must be positive.");
            return 1;
        }
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"synthetic-{mib}MiB.log");
        try
        {
            var total = Stopwatch.StartNew();
            var generate = Stopwatch.StartNew();
            SyntheticBenchmarkLog.Write(path, mib);
            generate.Stop();
            long fileBytes = new FileInfo(path).Length;

            var phase = Stopwatch.StartNew();
            Retained retained = Open(path);
            phase.Stop();
            var samples = new List<MemorySample>
            {
                Sample("afterOpen", null, phase.ElapsedMilliseconds, retained)
            };
            int lines = retained.Log.Lines.Count;
            int entries = retained.Log.Entries.Count;

            phase.Restart();
            ReplaceWithFilter(ref retained);
            phase.Stop();
            samples.Add(Sample("afterFilterAndHighlight", null, phase.ElapsedMilliseconds, retained));
            int filteredLines = retained.View.Count;
            int filteredEntries = retained.View.EntryCount;
            int highlightSpans = retained.Highlight!.Count;
            bool highlightLimited = retained.HighlightLimited;

            for (int refresh = 1; refresh <= refreshes; refresh++)
            {
                phase.Restart();
                ReplaceWithRefresh(ref retained, path);
                phase.Stop();
                samples.Add(Sample("afterRefresh", refresh, phase.ElapsedMilliseconds, retained));
            }
            total.Stop();

            MemorySample open = samples[0];
            List<MemorySample> refreshSamples = samples.Where(sample => sample.Phase == "afterRefresh").ToList();
            long workingSetMargin = MemoryGrowthCheck.WorkingSetMargin(open.WorkingSetBytes);
            MemoryGrowthCheck.MetricResult managed = MemoryGrowthCheck.Judge(
                MemoryGrowthCheck.ManagedHeapMetric,
                refreshSamples.Select(sample => sample.ManagedHeapBytes).ToArray(),
                MemoryGrowthCheck.ManagedHeapMargin(open.ManagedHeapBytes),
                MemoryGrowthCheck.ManagedHeapMarginRule);
            MemoryGrowthCheck.MetricResult committed = MemoryGrowthCheck.Judge(
                MemoryGrowthCheck.CommittedMetric,
                refreshSamples.Select(sample => sample.CommittedBytes).ToArray(),
                MemoryGrowthCheck.CommittedMargin(open.CommittedBytes),
                MemoryGrowthCheck.CommittedMarginRule);
            MemoryGrowthCheck.MetricResult privateBytes = JudgePrivate(
                refreshSamples.Select(sample => sample.PrivateBytes).ToArray(), open.PrivateBytes);
            MemoryGrowthCheck.MetricResult privateMinusCommitted = JudgeResidual(
                refreshSamples.Select(sample => sample.PrivateMinusCommittedBytes).ToArray(), open.PrivateMinusCommittedBytes);
            MemoryGrowthCheck.MetricResult[] metrics = [managed, committed, privateBytes, privateMinusCommitted];
            string? warning = MemoryGrowthCheck.WarningText(metrics, mib);
            string verdict = MemoryGrowthCheck.Summarize(metrics.Select(metric => metric.Verdict).ToArray());
            var report = new MemoryReport
            {
                Timestamp = DateTimeOffset.Now,
                Os = Environment.OSVersion.ToString(),
                Runtime = Environment.Version.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                RequestedMiB = mib,
                SyntheticFileName = Path.GetFileName(path),
                FileBytes = fileBytes,
                Lines = lines,
                Entries = entries,
                FilteredLines = filteredLines,
                FilteredEntries = filteredEntries,
                HighlightPhrase = HighlightPhrase,
                HighlightSpans = highlightSpans,
                HighlightLimited = highlightLimited,
                RefreshCount = refreshes,
                GenerateMs = generate.Elapsed.TotalMilliseconds,
                ElapsedMs = total.Elapsed.TotalMilliseconds,
                Layers = Layers,
                Gc = Gc,
                Filter = "threads 3, 7, and null; EntryFilter empty",
                ManagedHeapMarginBytes = managed.MarginBytes ?? 0,
                ManagedHeapMarginRule = managed.MarginRule,
                CommittedMarginBytes = committed.MarginBytes ?? 0,
                CommittedMarginRule = committed.MarginRule,
                PrivateBytesMarginBytes = privateBytes.MarginBytes,
                PrivateBytesMarginRule = privateBytes.MarginRule,
                PrivateMinusCommittedMarginBytes = privateMinusCommitted.MarginBytes,
                PrivateMinusCommittedMarginRule = privateMinusCommitted.MarginRule,
                WorkingSetMarginBytes = workingSetMargin,
                WorkingSetMarginRule = MemoryGrowthCheck.WorkingSetMarginRule,
                ManagedHeapVerdict = managed.Verdict,
                CommittedVerdict = committed.Verdict,
                PrivateBytesVerdict = privateBytes.Verdict,
                PrivateMinusCommittedVerdict = privateMinusCommitted.Verdict,
                Metrics = metrics.ToList(),
                Warning = warning is not null,
                WarningText = warning,
                Verdict = verdict,
                Samples = samples
            };
            File.WriteAllText(Path.Combine(folder, $"memory-{mib}MiB.json"), JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine(warning is null ? $"{mib} MiB: {verdict}" : "WARNING: " + warning);
            foreach (MemoryGrowthCheck.MetricResult metric in metrics)
                Console.WriteLine($"{mib} MiB {metric.Name}: {metric.Verdict}");
            GC.KeepAlive(retained);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            try
            {
                File.WriteAllText(Path.Combine(folder, $"memory-{mib}MiB.error.txt"), ex.ToString());
            }
            catch (Exception writeEx)
            {
                Console.Error.WriteLine(writeEx.Message);
            }
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Retained Open(string path)
    {
        LogData log = LogFileReader.ReadAsync(path).GetAwaiter().GetResult();
        int?[] threads = log.Threads.Select(static thread => thread.ThreadId).ToArray();
        LogProjection view = LogProjection.Create(log, threads);
        TextDocument document = CancellableDocumentFactory.Create(view.Text, CancellationToken.None, SilentProgress.Instance);
        return new Retained(log, view, document, null, false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReplaceWithFilter(ref Retained slot)
    {
        Retained next = ApplyFilter(slot.Log);
        slot = next;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReplaceWithRefresh(ref Retained slot, string path)
    {
        Retained next = Refresh(slot, path);
        slot = next;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Retained ApplyFilter(LogData log)
    {
        LogProjection view = LogProjection.Create(log, FilteredThreads);
        TextDocument document = CancellableDocumentFactory.Create(view.Text, CancellationToken.None, SilentProgress.Instance);
        HighlightBuild highlight = BuildHighlight(view);
        return new Retained(log, view, document, highlight.Index, highlight.Limited);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Retained Refresh(Retained current, string path)
    {
        LogData updated = LogFileReader.ReadAsync(path).GetAwaiter().GetResult();
        SourceLineRemap map = SourceLineRemap.Create(current.Log, updated);
        LogProjection view = LogProjection.CreateFiltered(updated, FilteredThreads, EntryFilter.Empty);
        TextDocument document = CancellableDocumentFactory.Create(view.Text, CancellationToken.None, SilentProgress.Instance);
        HighlightBuild highlight = BuildHighlight(view);
        GC.KeepAlive(map);
        return new Retained(updated, view, document, highlight.Index, highlight.Limited);
    }

    private static HighlightBuild BuildHighlight(LogProjection view)
    {
        LocatedSearchResults found = LogSearch.Find(view.Source, view.EntryIndexes, HighlightPhrase, new SearchOptions());
        var spans = new List<HighlightSpan>(found.Hits.Length);
        bool limited = found.Limited;
        foreach (LocatedSearchHit hit in found.Hits)
        {
            if (spans.Count == LogSearch.MaxHighlights)
            {
                limited = true;
                break;
            }
            if (view.GetDisplayOffset(hit.SourceOffset) is int offset && hit.Length <= view.Text.Length - offset)
                spans.Add(new HighlightSpan(offset, hit.Length, 0));
        }
        return new HighlightBuild(new HighlightIndex(spans), limited);
    }

    private static MemoryGrowthCheck.MetricResult JudgePrivate(long?[] refreshValues, long? afterOpen)
    {
        if (afterOpen is not long open || refreshValues.Any(value => value is null))
            return MemoryGrowthCheck.Insufficient(MemoryGrowthCheck.PrivateBytesMetric, MemoryGrowthCheck.PrivateBytesMarginRule);
        return MemoryGrowthCheck.Judge(
            MemoryGrowthCheck.PrivateBytesMetric,
            refreshValues.Select(value => value!.Value).ToArray(),
            MemoryGrowthCheck.PrivateBytesMargin(open),
            MemoryGrowthCheck.PrivateBytesMarginRule);
    }

    private static MemoryGrowthCheck.MetricResult JudgeResidual(long?[] refreshValues, long? afterOpen)
    {
        if (afterOpen is not long open || refreshValues.Any(value => value is null))
            return MemoryGrowthCheck.Insufficient(MemoryGrowthCheck.PrivateMinusCommittedMetric, MemoryGrowthCheck.PrivateMinusCommittedMarginRule);
        return MemoryGrowthCheck.Judge(
            MemoryGrowthCheck.PrivateMinusCommittedMetric,
            refreshValues.Select(value => value!.Value).ToArray(),
            MemoryGrowthCheck.PrivateMinusCommittedMargin(open),
            MemoryGrowthCheck.PrivateMinusCommittedMarginRule);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static MemorySample Sample(string phase, int? refresh, long elapsedMs, object rooted)
    {
        Collect();
        long managedHeap = GC.GetTotalMemory(false);
        GCMemoryInfo info = GC.GetGCMemoryInfo();
        long lohBytes = 0;
        // Generation 3 is the large object heap. Generation 4, when present, is the pinned object heap.
        ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
        if (generations.Length > 3)
            lohBytes = generations[3].SizeAfterBytes;
        Process process = Process.GetCurrentProcess();
        process.Refresh();
        long? privateBytes = null;
        bool privateBytesAvailable = true;
        try
        {
            privateBytes = process.PrivateMemorySize64;
        }
        catch (PlatformNotSupportedException)
        {
            privateBytesAvailable = false;
        }
        long? privateMinusCommitted = privateBytes is long privateValue ? privateValue - info.TotalCommittedBytes : null;
        GC.KeepAlive(rooted);
        return new MemorySample
        {
            Phase = phase,
            Refresh = refresh,
            WorkingSetBytes = process.WorkingSet64,
            PeakWorkingSetBytes = process.PeakWorkingSet64,
            PrivateBytes = privateBytes,
            PrivateBytesAvailable = privateBytesAvailable,
            PrivateMinusCommittedBytes = privateMinusCommitted,
            ManagedHeapBytes = managedHeap,
            CommittedBytes = info.TotalCommittedBytes,
            LohBytes = lohBytes,
            FragmentedBytes = info.FragmentedBytes,
            ElapsedMs = elapsedMs
        };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Collect()
    {
        Task.Run(() =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }).GetAwaiter().GetResult();
    }

    private sealed class SilentProgress : IProgress<WorkProgress>
    {
        public static SilentProgress Instance { get; } = new();
        public void Report(WorkProgress value) { }
    }

    private readonly record struct HighlightBuild(HighlightIndex Index, bool Limited);

    private sealed class Retained(LogData log, LogProjection view, TextDocument document, HighlightIndex? highlight, bool highlightLimited)
    {
        public LogData Log { get; } = log;
        public LogProjection View { get; } = view;
        public TextDocument Document { get; } = document;
        public HighlightIndex? Highlight { get; } = highlight;
        public bool HighlightLimited { get; } = highlightLimited;
    }

    private sealed class MemorySample
    {
        public string Phase { get; init; } = "";
        public int? Refresh { get; init; }
        public long WorkingSetBytes { get; init; }
        public long PeakWorkingSetBytes { get; init; }
        public long? PrivateBytes { get; init; }
        public bool PrivateBytesAvailable { get; init; }
        public long? PrivateMinusCommittedBytes { get; init; }
        public long ManagedHeapBytes { get; init; }
        public long CommittedBytes { get; init; }
        public long LohBytes { get; init; }
        public long FragmentedBytes { get; init; }
        public long ElapsedMs { get; init; }
    }

    private sealed class MemoryReport
    {
        public int Schema { get; init; } = 2;
        public DateTimeOffset Timestamp { get; init; }
        public string Os { get; init; } = "";
        public string Runtime { get; init; } = "";
        public int ProcessorCount { get; init; }
        public int RequestedMiB { get; init; }
        public string SyntheticFileName { get; init; } = "";
        public long FileBytes { get; init; }
        public int Lines { get; init; }
        public int Entries { get; init; }
        public int FilteredLines { get; init; }
        public int FilteredEntries { get; init; }
        public string HighlightPhrase { get; init; } = "";
        public int HighlightSpans { get; init; }
        public bool HighlightLimited { get; init; }
        public int RefreshCount { get; init; }
        public double GenerateMs { get; init; }
        public double ElapsedMs { get; init; }
        public string Layers { get; init; } = "";
        public string Gc { get; init; } = "";
        public string Filter { get; init; } = "";
        public long ManagedHeapMarginBytes { get; init; }
        public string ManagedHeapMarginRule { get; init; } = "";
        public long CommittedMarginBytes { get; init; }
        public string CommittedMarginRule { get; init; } = "";
        public long? PrivateBytesMarginBytes { get; init; }
        public string PrivateBytesMarginRule { get; init; } = "";
        public long? PrivateMinusCommittedMarginBytes { get; init; }
        public string PrivateMinusCommittedMarginRule { get; init; } = "";
        public long WorkingSetMarginBytes { get; init; }
        public string WorkingSetMarginRule { get; init; } = "";
        public string ManagedHeapVerdict { get; init; } = "";
        public string CommittedVerdict { get; init; } = "";
        public string PrivateBytesVerdict { get; init; } = "";
        public string PrivateMinusCommittedVerdict { get; init; } = "";
        public List<MemoryGrowthCheck.MetricResult> Metrics { get; init; } = [];
        public bool Warning { get; init; }
        public string? WarningText { get; init; }
        public string Verdict { get; init; } = "";
        public List<MemorySample> Samples { get; init; } = [];
    }
}
