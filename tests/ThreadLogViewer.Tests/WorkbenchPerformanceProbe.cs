using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ThreadLogViewer.App;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.Tests;

/// <summary>Opt-in synthetic, unhosted WPF measurements. Never shows a window or uses native input.</summary>
public static class WorkbenchPerformanceProbe
{
    public static async Task<int> RunAsync(string outputDirectory, int mib)
    {
        if (mib is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(mib), "Use 1 through 256 MiB.");
        string folder = ValidateOutputDirectory(outputDirectory);
        Directory.CreateDirectory(folder);
        var lifetime = new CancellationTokenSource();
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/ThreadLogViewer;component/Styles/Controls.xaml", UriKind.Relative)
                });
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex) { ready.TrySetException(ex); }
        }) { IsBackground = true, Name = "Synthetic workbench performance dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        var records = new ConcurrentQueue<CaseRecord>();
        var snapshots = new ConcurrentQueue<object>();
        var retention = new ConcurrentQueue<object>();
        string? failure = null;
        Dispatcher? dispatcher = null;
        Probe? probe = null;
        var total = Stopwatch.StartNew();
        try
        {
            dispatcher = await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
            probe = new Probe(folder, mib, dispatcher, lifetime.Token, records, snapshots, retention);
            await dispatcher.InvokeAsync(probe.RunAsync).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(180));
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            Console.WriteLine("[workbench-probe] incomplete: " + failure);
        }
        finally
        {
            lifetime.Cancel();
            dispatcher?.BeginInvokeShutdown(DispatcherPriority.Background);
        }
        var report = new
        {
            Timestamp = DateTimeOffset.Now, Runtime = Environment.Version.ToString(), Environment.OSVersion,
            Environment.ProcessorCount, ProcessId = Environment.ProcessId, RequestedSingleFileMiB = mib,
            ElapsedMs = total.Elapsed.TotalMilliseconds, Completed = failure is null,
            Successful = failure is null && records.All(r => r.Error is null) && probe?.CancellationConfirmed == true,
            Failure = failure, ActualCancellationConfirmed = probe?.CancellationConfirmed,
            LiveTabResourcesCollected = probe?.LiveTabResourcesCollected,
            InterruptedCase = probe?.CurrentCase,
            Scope = "Synthetic unhosted WPF MainWindow APIs, background work, UI publication, Measure/Arrange/EnsureVisualLines; no visible window, native input, clipboard, compositor frame rate, monitor DPI or user settings. One process run; generated files may be in the OS cache.",
            HeartbeatScope = "A worker posts timestamp probes at DispatcherPriority.Input every 25 ms. Delays are synthetic dispatcher queue latency, not native input latency or rendered frame time. p95 describes probes in one operation, not repeated-run statistics.",
            MemoryScope = "Sampled process working/private bytes plus approximate managed heap. PeakWorkingSetBytes is cumulative for this process; per-case sampled peaks may miss short spikes. Post-close forced GC runs after a separate window lifecycle helper completes and records a weak-reference collection check. Remaining deltas can include async state/awaiter references and framework caches; they do not establish a leak or prove successful reclamation.",
            DpiScope = "Offscreen bitmap raster densities 120/144/192 DPI at a fixed 1040x600 DIP layout. Visual-tree DPI is recorded separately. These are not real monitor DPI, per-monitor transitions, title bar, focus, popup or input checks.",
            Cases = records.ToArray(), Snapshots = snapshots.ToArray(), RetentionEstimates = retention.ToArray(),
            FinalMemory = Memory()
        };
        await File.WriteAllTextAsync(Path.Combine(folder, "workbench-performance.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        Console.WriteLine("[workbench-probe] report: " + Path.Combine(folder, "workbench-performance.json"));
        lifetime.Dispose();
        return failure is null && records.All(r => r.Error is null) && probe?.CancellationConfirmed == true ? 0 : 2;
    }

    private sealed class Probe(string folder, int mib, Dispatcher dispatcher, CancellationToken lifetime,
        ConcurrentQueue<CaseRecord> records, ConcurrentQueue<object> snapshots, ConcurrentQueue<object> retention)
    {
        public string? CurrentCase { get; private set; }
        public bool CancellationConfirmed { get; private set; }
        public bool LiveTabResourcesCollected { get; private set; }

        public async Task RunAsync()
        {
            string large = Path.Combine(folder, $"synthetic-single-{mib}MiB.log");
            GenerateStandard(large, mib, 0);
            await WithWindow("single-file", async window =>
            {
                await Measure("single-file-open-and-layout", async () =>
                {
                    bool opened = await Open(window, large);
                    await Layout(window, 1360, 860);
                    return new { Opened = opened, InputBytes = new FileInfo(large).Length, State = State(window) };
                });
                await Measure("single-file-filter-and-layout", async () =>
                {
                    Func<ThreadItem, bool> selected = item => item.Id is 3 or 7;
                    await (Task)Invoke(window, "SetThreadsAsync", selected)!;
                    await Layout(window, 1360, 860);
                    return new { IncludesDebounceMs = 100, State = State(window) };
                });
                string small = Path.Combine(folder, "synthetic-live-close-baseline.log");
                WriteNew(small, "[000:00:00.000] [T1] synthetic small tab remains open\n");
                await Open(window, small);
                await Layout(window, 1360, 860);
                var closedResources = CloseFirstTabAndClearRecovery(window);
                await Measure("live-window-close-large-tab-and-release-recovery", async () =>
                {
                    // The same production window remains open with its small tab. Only the
                    // documented closed-tab recovery cache is cleared; no source fields are reset.
                    await Layout(window, 1360, 860);
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    await Task.Delay(250, lifetime);
                    await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); });
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    LiveTabResourcesCollected = !closedResources.Source.IsAlive && !closedResources.Projection.IsAlive && !closedResources.Document.IsAlive;
                    return new { SourceCollected = !closedResources.Source.IsAlive,
                        ProjectionCollected = !closedResources.Projection.IsAlive,
                        DocumentCollected = !closedResources.Document.IsAlive,
                        BeforeClose = closedResources.BeforeClose,
                        AfterCloseAndForcedGc = Memory(), State = State(window),
                        Scope = "Same live MainWindow; close the large tab through its production API, explicitly clear closed-tab recovery, retain one small tab, then test weak references after idle and forced GC. Working set can remain reserved by the runtime." };
                });
            });

            string[] multiple = Enumerable.Range(0, 3).Select(i => Path.Combine(folder, $"synthetic-tab-{i + 1}-10MiB.log")).ToArray();
            for (int i = 0; i < multiple.Length; i++) GenerateStandard(multiple[i], 10, i * 16);
            await WithWindow("three-tabs", async window =>
            {
                for (int i = 0; i < multiple.Length; i++)
                {
                    string path = multiple[i];
                    await Measure("multi-tab-open-" + (i + 1), async () =>
                    {
                        bool opened = await Open(window, path);
                        await Layout(window, 1360, 860);
                        return new { Opened = opened, InputBytes = new FileInfo(path).Length, State = State(window) };
                    });
                }
                await Measure("multi-tab-switch-and-layout", async () =>
                {
                    var tabs = Control<ListBox>(window, "SessionTabs");
                    for (int index = 0; index < tabs.Items.Count; index++)
                    {
                        tabs.SelectedIndex = index;
                        await Layout(window, 1360, 860);
                    }
                    return State(window);
                });
            });

            string longLine = Path.Combine(folder, "synthetic-long-line-1MiB.log");
            WriteNew(longLine, "[000:00:00.000] [T1] synthetic " + new string('x', 1024 * 1024) + " target\n");
            await WithWindow("long-line", async window =>
            {
                await Measure("long-line-open-no-wrap-and-layout", async () =>
                {
                    Control<ToggleButton>(window, "WrapBox").IsChecked = false;
                    bool opened = await Open(window, longLine);
                    await Layout(window, 1040, 600);
                    return new { Opened = opened, InputBytes = new FileInfo(longLine).Length, State = State(window) };
                });
                await Measure("long-line-wrap-and-layout", async () =>
                {
                    Control<ToggleButton>(window, "WrapBox").IsChecked = true;
                    await Layout(window, 1040, 600);
                    return State(window);
                });
                await Measure("long-line-search-last-chunk-and-layout", async () =>
                {
                    await Search(window, "target");
                    var hit = Field<LocatedSearchHit[]>(window, "searchHits").Single();
                    await (Task)Invoke(window, "NavigateHitAsync", hit)!;
                    await Layout(window, 1040, 600);
                    var editor = Control<TextEditor>(window, "Editor");
                    return new { SelectedText = editor.SelectedText, SourceOffset = hit.SourceOffset,
                        RawDocumentLength = editor.Document.TextLength,
                        VisualColumns = editor.TextArea.TextView.VisualLines.Sum(line => line.VisualLength), State = State(window) };
                });
            });

            string many = Path.Combine(folder, "synthetic-100001-search-records.log");
            GenerateManyMatches(many);
            await WithWindow("many-results", async window =>
            {
                await Measure("many-results-open-and-layout", async () =>
                {
                    bool opened = await Open(window, many);
                    await Layout(window, 1360, 860);
                    return new { Opened = opened, InputBytes = new FileInfo(many).Length, State = State(window) };
                });
                await Measure("many-results-search-publication-and-layout", async () =>
                {
                    await Search(window, "target");
                    await Layout(window, 1360, 860);
                    if (Field<LocatedSearchHit[]>(window, "searchHits").Length != LogSearch.MaxHighlights ||
                        !Field<bool>(window, "searchLimited"))
                        throw new InvalidOperationException("The synthetic 100001-hit search did not reach the expected result cap.");
                    return new { IncludesDebounceMs = 180, ExpectedOccurrences = 100001,
                        HitCount = Field<LocatedSearchHit[]>(window, "searchHits").Length,
                        Limited = Field<bool>(window, "searchLimited"), State = State(window) };
                });
                await Measure("many-results-synthetic-scroll-and-navigation", async () =>
                {
                    var list = Control<ListBox>(window, "ResultsList");
                    list.ScrollIntoView(list.Items[list.Items.Count - 1]);
                    await Layout(window, 1360, 860);
                    await (Task)Invoke(window, "NavigateSearchAsync", true)!;
                    await Layout(window, 1360, 860);
                    return new { Scope = "API-driven scroll and search navigation, not mouse/keyboard input.", State = State(window) };
                });
                foreach (bool dark in new[] { true, false })
                {
                    Control<ComboBox>(window, "ThemeBox").SelectedIndex = dark ? 0 : 1;
                    await Layout(window, 1040, 600);
                    foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 }) SaveSnapshot(window, 1040, 600, scale, dark);
                }
            });

            string baseline = Path.Combine(folder, "synthetic-cancel-baseline.log");
            WriteNew(baseline, "[000:00:00.000] [T1] synthetic previous document\n");
            await WithWindow("cancellation", async window =>
            {
                await Open(window, baseline);
                await Layout(window, 1360, 860);
                await Measure("cancel-open-after-real-reader-progress", async () =>
                {
                    var previous = Field<LogData>(window, "data");
                    var workerStarted = new TaskCompletionSource<WorkProgress>(TaskCreationOptions.RunContinuationsAsynchronously);
                    CancellationToken workerToken = default;
                    Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> loader = (token, progress) =>
                    {
                        workerToken = token;
                        return LogFileReader.ReadAsync(large, EncodingMode.Auto, token, new RelayProgress(progress, p =>
                        {
                            if (p.Percent is > 0 and < 100) workerStarted.TrySetResult(p);
                        }));
                    };
                    var work = (Task<bool>)Invoke(window, "LoadAsync", loader, "synthetic cancellation measurement", "synthetic load failure")!;
                    var clock = Stopwatch.StartNew();
                    Task first = await Task.WhenAny(workerStarted.Task, work);
                    if (first == work)
                        return new { Outcome = "completed-before-worker-progress-observed", Completed = await work,
                            WorkerObserved = false, RequestToHandlerMs = (double?)null, HandlerToCompletionMs = (double?)null };
                    WorkProgress phase = await workerStarted.Task;
                    await Task.Delay(10, lifetime);
                    if (work.IsCompleted)
                        return new { Outcome = "completed-before-cancel-request", Completed = await work,
                            WorkerObserved = true, RequestToHandlerMs = (double?)null, HandlerToCompletionMs = (double?)null };
                    double requestedAt = clock.Elapsed.TotalMilliseconds, handledAt = 0;
                    bool completedBeforeHandler = false;
                    await dispatcher.InvokeAsync(() =>
                    {
                        handledAt = clock.Elapsed.TotalMilliseconds;
                        completedBeforeHandler = work.IsCompleted;
                        if (!completedBeforeHandler) Invoke(window, "Cancel_Click", window, new RoutedEventArgs());
                    }, DispatcherPriority.Input);
                    bool completed = await work;
                    double finishedAt = clock.Elapsed.TotalMilliseconds;
                    await Layout(window, 1360, 860);
                    bool previousPreserved = ReferenceEquals(previous, Field<LogData>(window, "data"));
                    CancellationConfirmed = !completedBeforeHandler && !completed && workerToken.IsCancellationRequested && previousPreserved;
                    return (object)new { Outcome = completedBeforeHandler ? "completed-before-cancel-handler" :
                            !completed && workerToken.IsCancellationRequested ? "cancelled" : "completed-after-cancel-request",
                        Completed = completed, WorkerObserved = true, WorkerPhase = phase.Phase, WorkerPercent = phase.Percent,
                        CancellationTokenRequested = workerToken.IsCancellationRequested,
                        RequestToHandlerMs = handledAt - requestedAt,
                        HandlerToCompletionMs = completedBeforeHandler ? (double?)null : finishedAt - handledAt,
                        RequestToCompletionMs = finishedAt - requestedAt,
                        PreviousSourcePreserved = previousPreserved, State = State(window) };
                });
            });
        }

        private async Task WithWindow(string name, Func<MainWindow, Task> action)
        {
            MemorySnapshot before = Memory();
            WeakReference closedWindow = await RunWindowLifecycle(name, action);
            // A separate completed lifecycle method keeps the GC measurement out of the frame
            // that owned the window. No production session/document state is cleared here.
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.Run(() => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); });
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            MemorySnapshot after = Memory();
            retention.Enqueue(new { Scenario = name, Before = before, AfterCloseAndForcedGc = after,
                ManagedDeltaBytes = after.ManagedHeapBytes - before.ManagedHeapBytes,
                ClosedWindowCollected = !closedWindow.IsAlive,
                Scope = "Approximate post-close managed delta after the lifecycle helper completes. Weak-reference collection is recorded independently; remaining async state/awaiter references or WPF/JIT/font caches are possible, so this is not a leak conclusion or a reclamation guarantee." });
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private async Task<WeakReference> RunWindowLifecycle(string name, Func<MainWindow, Task> action)
        {
            MainWindow? window = null;
            WeakReference? closedWindow = null;
            try
            {
                lifetime.ThrowIfCancellationRequested();
                string settings = Path.Combine(folder, "settings-" + name);
                Directory.CreateDirectory(settings);
                window = new MainWindow(settings, false);
                closedWindow = new WeakReference(window);
                await action(window);
            }
            finally
            {
                window?.Close();
                if (Application.Current is { } application && ReferenceEquals(application.MainWindow, window))
                    application.MainWindow = null;
                window = null;
            }
            return closedWindow ?? throw new InvalidOperationException("The synthetic window lifecycle did not create a window.");
        }

        private async Task Measure(string name, Func<Task<object>> action)
        {
            Console.WriteLine("[workbench-probe] begin " + name);
            CurrentCase = name;
            var delays = new ConcurrentQueue<double>();
            MemorySnapshot before = Memory();
            long sampledWorkingPeak = before.WorkingSetBytes, sampledPrivatePeak = before.PrivateBytes;
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            var pulse = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        long sent = Stopwatch.GetTimestamp();
                        _ = dispatcher.BeginInvoke(new Action(() => delays.Enqueue(Stopwatch.GetElapsedTime(sent).TotalMilliseconds)), DispatcherPriority.Input);
                        MemorySnapshot sample = Memory();
                        sampledWorkingPeak = Math.Max(sampledWorkingPeak, sample.WorkingSetBytes);
                        sampledPrivatePeak = Math.Max(sampledPrivatePeak, sample.PrivateBytes);
                        await Task.Delay(25, stop.Token);
                    }
                }
                catch (OperationCanceledException) { }
            });
            var clock = Stopwatch.StartNew();
            object? details = null;
            string? error = null;
            try { details = await action(); }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
            double elapsed = clock.Elapsed.TotalMilliseconds;
            stop.Cancel();
            await pulse;
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            double[] sorted = delays.Order().ToArray();
            MemorySnapshot after = Memory();
            records.Enqueue(new CaseRecord(name, elapsed, before, after,
                Math.Max(sampledWorkingPeak, after.WorkingSetBytes), Math.Max(sampledPrivatePeak, after.PrivateBytes),
                sorted.Length, sorted.Length == 0 ? null : sorted[(int)Math.Ceiling(sorted.Length * 0.95) - 1],
                sorted.Length == 0 ? null : sorted[^1], details, error));
            Console.WriteLine("[workbench-probe] end " + name + " " + elapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms" + (error is null ? "" : " failed: " + error));
            CurrentCase = null;
            lifetime.ThrowIfCancellationRequested();
        }

        private void SaveSnapshot(MainWindow window, int dipWidth, int dipHeight, double scale, bool dark)
        {
            string name = "workbench-small-" + (dark ? "dark" : "light") + "-raster-" +
                (scale * 100).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ".png";
            int pixelWidth = (int)Math.Ceiling(dipWidth * scale), pixelHeight = (int)Math.Ceiling(dipHeight * scale);
            var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render((FrameworkElement)window.Content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new FileStream(Path.Combine(folder, name), FileMode.CreateNew, FileAccess.Write);
            encoder.Save(output);
            var visualDpi = VisualTreeHelper.GetDpi((FrameworkElement)window.Content);
            var grid = Control<Grid>(window, "LogContentGrid");
            var editor = Control<TextEditor>(window, "Editor");
            var results = Control<Border>(window, "ResultsPanel");
            Rect editorBounds = new(editor.TranslatePoint(new Point(), grid), editor.RenderSize);
            Rect resultsBounds = new(results.TranslatePoint(new Point(), grid), results.RenderSize);
            snapshots.Enqueue(new { File = name, Theme = dark ? "Dark" : "Light", LayoutWidthDip = dipWidth, LayoutHeightDip = dipHeight,
                RasterScale = scale, PixelWidth = pixelWidth, PixelHeight = pixelHeight, RasterDpi = 96 * scale,
                VisualTreeScaleX = visualDpi.DpiScaleX, VisualTreeScaleY = visualDpi.DpiScaleY,
                EditorHeightDip = editor.ActualHeight, ResultsHeightDip = results.ActualHeight,
                PaneOverlap = resultsBounds.Top < editorBounds.Bottom - 1, WindowVisible = window.IsVisible,
                Scope = "Offscreen raster density only; not real monitor DPI or native window layout/input validation." });
        }
    }

    private sealed class RelayProgress(IProgress<WorkProgress> target, Action<WorkProgress> observe) : IProgress<WorkProgress>
    {
        public void Report(WorkProgress value) { observe(value); target.Report(value); }
    }

    private sealed record MemorySnapshot(long WorkingSetBytes, long PrivateBytes, long PeakWorkingSetBytes, long ManagedHeapBytes);
    private sealed record ClosedResourceReferences(WeakReference Source, WeakReference Projection, WeakReference Document, MemorySnapshot BeforeClose);
    private sealed record CaseRecord(string Name, double ElapsedMs, MemorySnapshot Before, MemorySnapshot After,
        long SampledPeakWorkingSetBytes, long SampledPeakPrivateBytes, int DispatcherProbeCount,
        double? DispatcherP95DelayMs, double? DispatcherMaximumDelayMs, object? Details, string? Error);

    private static MemorySnapshot Memory()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return new(process.WorkingSet64, process.PrivateMemorySize64, process.PeakWorkingSet64, GC.GetTotalMemory(false));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ClosedResourceReferences CloseFirstTabAndClearRecovery(MainWindow window)
    {
        var tabs = Control<ListBox>(window, "SessionTabs");
        object session = tabs.Items[0];
        object source = session.GetType().GetProperty("Source")!.GetValue(session)!;
        object projection = session.GetType().GetProperty("View")!.GetValue(session)!;
        object document = session.GetType().GetProperty("Document")!.GetValue(session)!;
        var references = new ClosedResourceReferences(new(source), new(projection), new(document), Memory());
        Invoke(window, "CloseSession", session);
        object recovery = typeof(MainWindow).GetField("closedSessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
        recovery.GetType().GetMethod("Clear")!.Invoke(recovery, null);
        return references;
    }

    private static async Task Layout(MainWindow window, double width, double height)
    {
        window.Width = width; window.Height = height;
        var content = (FrameworkElement)window.Content;
        for (int pass = 0; pass < 3; pass++)
        {
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
        }
        Control<TextEditor>(window, "Editor").TextArea.TextView.EnsureVisualLines();
        if (window.IsVisible) throw new InvalidOperationException("The probe must never show a window.");
    }

    private static async Task<bool> Open(MainWindow window, string path)
    {
        bool opened = await (Task<bool>)Invoke(window, "OpenAsync", path, EncodingMode.Auto)!;
        if (!opened) throw new InvalidOperationException("The synthetic file did not open: " + Path.GetFileName(path));
        return true;
    }
    private static async Task Search(MainWindow window, string query)
    {
        Control<Border>(window, "SearchBar").Visibility = Visibility.Visible;
        Control<TextBox>(window, "SearchBox").Text = query;
        await (Task)Invoke(window, "SearchAsync")!;
    }
    private static object State(MainWindow window) => new
    {
        Tabs = Control<ListBox>(window, "SessionTabs").Items.Count,
        SourceLines = Field<LogData>(window, "data").Lines.Count,
        DisplayLines = Field<LogProjection>(window, "projection").Count,
        SearchHits = Field<LocatedSearchHit[]>(window, "searchHits").Length,
        EditorHeightDip = Control<TextEditor>(window, "Editor").ActualHeight,
        ResultsHeightDip = Control<Border>(window, "ResultsPanel").ActualHeight,
        WordWrap = Control<TextEditor>(window, "Editor").WordWrap, WindowVisible = window.IsVisible
    };
    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);
    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
    private static object? Invoke(MainWindow window, string name, params object?[] arguments)
    {
        try { return typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, arguments); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static string ValidateOutputDirectory(string value)
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "ThreadLogViewer.slnx"))) root = Path.GetDirectoryName(root);
        if (root is null) throw new IOException("Run the probe from this project build output.");
        string allowed = Path.GetFullPath(Path.Combine(root, "TestResults")) + Path.DirectorySeparatorChar;
        string folder = Path.GetFullPath(value);
        if (!folder.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Probe output must be a new folder under the project's TestResults directory.");
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("Existing probe output is preserved; choose a new empty folder.");
        return folder;
    }

    private static void GenerateStandard(string path, int mib, int threadBase)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
        long bytes = 0, target = mib * 1024L * 1024;
        for (int index = 0; bytes < target; index++)
        {
            string line = $"[{index / 3600:000}:{index / 60 % 60:00}:{index % 60:00}.000] [T{threadBase + index % 16}] synthetic request {index:00000000} status=OK 한글";
            writer.Write(line); writer.Write('\n'); bytes += Encoding.UTF8.GetByteCount(line) + 1;
        }
    }

    private static void GenerateManyMatches(string path)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536);
        for (int index = 0; index < 100001; index++)
        { writer.Write($"[{index / 3600:000}:{index / 60 % 60:00}:{index % 60:00}.000] [T1] synthetic target record {index:000000}\n"); }
    }

    private static void WriteNew(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(text);
    }
}
