using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Microsoft.Win32;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow : Window
{
    private readonly LatestOperation work = new();
    private readonly LatestOperation searchWork = new();
    private readonly ThreadBackgroundRenderer threadRenderer = new();
    private readonly SearchHighlightRenderer searchRenderer = new();
    private readonly OriginalLineMargin margin = new();
    private LogData? data;
    private LogProjection? projection;
    private List<ThreadItem> threadItems = [];
    private string? requestedPath;
    private bool suppressFilters, busy;
    private int searchIndex = -1;
    private bool searchLimited;
    private bool viewReady;
    private WorkbenchTheme theme = new(true);
    private const string AppTitle = "ThreadLog Viewer v0.6.3";

    public MainWindow() : this(null, true) { }
    public MainWindow(string? settingsDirectory, bool persistSettings)
    {
        InitializeComponent();
        Editor.TextArea.AllowDrop = true;
        Editor.TextArea.LeftMargins.Add(margin);
        margin.LineClicked += SelectWholeDisplayLine;
        InitializeLineSelection();
        Editor.TextArea.TextView.BackgroundRenderers.Add(threadRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(keywordRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(searchRenderer);
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = false;
        Editor.TextArea.SelectionCornerRadius = 0;
        InitializeFeatures(settingsDirectory, persistSettings);
        InitializeSessions();
        viewReady = true;
        InitializeAnalysis();
        ApplyTheme();
        ApplyTypography();
        UpdateMenus();
        SourceInitialized += (_, _) => theme.ApplyTitleBar(this);
        Closed += (_, _) => { fileBatchVersion++; viewReady = false; DisposeFeatures(); work.Dispose(); searchWork.Dispose(); };
        Loaded += async (_, _) =>
        {
            string[] paths = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (paths.Length > 0) await OpenFilesAsync(paths);
        };
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "로그 파일 열기 — 여러 파일 선택 가능", Filter = "로그 파일 (*.log;*.txt)|*.log;*.txt|모든 파일 (*.*)|*.*", CheckFileExists = true, Multiselect = true };
        if (dialog.ShowDialog(this) == true) await OpenFilesAsync(dialog.FileNames);
    }
    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        await ReloadCurrentAsync(EncodingMode.Cp949);
    }
    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private async void PasteLog_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private Task PasteAsync() => PasteFromDataObjectAsync(Clipboard.GetDataObject);
    private void Copy_Click(object sender, RoutedEventArgs e) => Editor.Copy();
    private void SelectText_Click(object sender, RoutedEventArgs e) { Editor.Focus(); Editor.SelectAll(); }
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; // Keep AvalonEdit's editing/drop handlers from consuming the file.
        e.Effects = DragDropEffects.None;
        try
        {
            if (e.AllowedEffects.HasFlag(DragDropEffects.Copy) && LogTransfer.ReadDroppedFiles(e.Data).Count > 0)
                e.Effects = DragDropEffects.Copy;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException or InvalidDataException or OutOfMemoryException) { }
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return;
        try
        {
            var paths = LogTransfer.ReadDroppedFiles(e.Data);
            e.Effects = DragDropEffects.Copy; // Opening a file never moves or deletes the source.
            await OpenFilesAsync(paths);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or InvalidDataException or ArgumentException or OutOfMemoryException)
        { ShowError("끌어 놓은 파일을 열 수 없습니다", ex); }
    }
    private (long Version, CancellationToken Token, IProgress<WorkProgress> Progress) BeginWork(string label, bool lockFilters)
    {
        var operation = work.Begin();
        busy = true;
        WorkPanel.Visibility = Visibility.Visible;
        ExportButton.IsEnabled = false;
        SetFilterLock(lockFilters);
        CancelButton.Visibility = ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        OperationStatus.Text = label;
        UpdateMenus();
        var progress = new Progress<WorkProgress>(p =>
        {
            if (!work.IsCurrent(operation.Version) || operation.Token.IsCancellationRequested || !busy) return;
            OperationStatus.Text = $"{p.Phase}… {p.Percent:F0}%";
            ProgressBar.Value = p.Percent;
        });
        return (operation.Version, operation.Token, progress);
    }
    private void FinishWork(long version)
    {
        if (!work.IsCurrent(version)) return;
        busy = false;
        WorkPanel.Visibility = Visibility.Collapsed;
        SetFilterLock(false);
        ExportButton.IsEnabled = projection is not null && !IsBlankSession;
        CancelButton.Visibility = ProgressBar.Visibility = Visibility.Collapsed;
        UpdateMenus();
    }
    private static TextDocument PrepareDocument(string text, CancellationToken token, IProgress<WorkProgress> progress)
    {
        token.ThrowIfCancellationRequested();
        progress.Report(new("화면 문서 준비", 0));
        var document = new TextDocument(text);
        document.UndoStack.SizeLimit = 0;
        document.SetOwnerThread(null);
        token.ThrowIfCancellationRequested();
        progress.Report(new("화면 문서 준비", 100));
        return document;
    }
    private Task<bool> OpenAsync(string path, EncodingMode mode)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { ShowError("파일 경로를 읽을 수 없습니다", ex); return Task.FromResult(false); }
        var existing = sessions.FirstOrDefault(s => s.Scope is null && s.Source.SourcePath is { } sourcePath &&
            string.Equals(sourcePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && mode == EncodingMode.Auto)
        { if (existing == activeSession && busy) CancelSessionWork(); ActivateSession(existing); return Task.FromResult(true); }
        return LoadIntoSessionAsync((token, progress) => LogFileReader.ReadAsync(fullPath, mode, token, progress),
            "파일 읽기 준비…", "파일을 열 수 없습니다", existing);
    }
    private Task ReloadCurrentAsync(EncodingMode mode)
    {
        if (ActiveScope is not null || activeSession?.Source.SourcePath is not { } path) return Task.CompletedTask;
        return LoadIntoSessionAsync((token, progress) => LogFileReader.ReadAsync(path, mode, token, progress),
            "인코딩으로 다시 읽기…", "파일을 다시 읽을 수 없습니다", activeSession);
    }
    private Task<bool> LoadAsync(Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load,
        string startingMessage, string errorTitle) => LoadIntoSessionAsync(load, startingMessage, errorTitle, null);

    private async Task<bool> LoadIntoSessionAsync(Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load,
        string startingMessage, string errorTitle, LogSession? replaceSession)
    {
        if (replaceSession is null && IsBlankSession) replaceSession = activeSession;
        if (busy) RestoreFilters();
        var op = BeginWork(startingMessage, true);
        var timer = Stopwatch.StartNew();
        try
        {
            var result = await Task.Run(async () =>
            {
                var loaded = await load(op.Token, op.Progress);
                var view = LogProjection.Create(loaded, loaded.Threads.Select(t => t.ThreadId), op.Token, op.Progress);
                return (loaded, view, document: PrepareDocument(view.Text, op.Token, op.Progress));
            }, op.Token);
            op.Token.ThrowIfCancellationRequested();
            if (!work.IsCurrent(op.Version)) return false;
            CommitLoadedSession(result.loaded, result.view, result.document, replaceSession);
            OperationStatus.Text = $"열기 완료 · {timer.Elapsed.TotalSeconds:F2}초 · 읽기 전용";
            return true;
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) { RestoreFilters(); OperationStatus.Text = "열기 취소 · 이전 화면 유지"; } }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or OutOfMemoryException)
        {
            if (work.IsCurrent(op.Version)) { RestoreFilters(); ShowError(errorTitle, ex); }
        }
        finally { FinishWork(op.Version); }
        return false;
    }
    private void PublishView(LogProjection view, TextDocument document, bool preservePosition = true)
    {
        positionMovedToNearest = false;
        var anchor = preservePosition ? CapturePosition() : null;
        restoringPosition = true;
        ResetLineSelectionGesture();
        searchWork.Cancel();
        searchRenderer.Index = HighlightIndex.Empty;
        highlightWork.Cancel();
        selectionWork.Cancel();
        projection = view;
        viewVersion++;
        threadRenderer.Projection = margin.Projection = view;
        UpdateLineActionIndicator();
        document.SetOwnerThread(Thread.CurrentThread);
        Editor.Document = document;
        RefreshLineSelectionIndicator();
        if (anchor is not null) RestorePosition(anchor);
        restoringPosition = false;
        UpdatePosition();
        RefreshBookmarks(); UpdateTime();
        margin.InvalidateMeasure(); margin.InvalidateVisual();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        int totalLines = ActiveScope is { } scope ? scope.LastLineIndex - scope.FirstLineIndex + 1 : view.Source.Lines.Count;
        int totalEntries = ActiveScope is { } entriesScope ? view.Source.Lines[entriesScope.LastLineIndex].EntryIndex - view.Source.Lines[entriesScope.FirstLineIndex].EntryIndex + 1 : view.Source.Entries.Count;
        CountStatus.Text = $"{view.EntryCount:N0}/{totalEntries:N0}건 · 표시 {view.Count:N0}/{totalLines:N0}줄";
        EmptyHint.Text = view.Source.Lines.Count == 0 ? "빈 파일입니다." : "표시할 기록이 없습니다. 필터 조건을 확인하세요.";
        EmptyPanel.Visibility = view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyDetail.Text = view.Source.Lines.Count == 0 ? "다른 파일을 열거나 Ctrl+V로 로그를 붙여넣으세요." : "전체 선택 또는 왼쪽 스레드 체크박스를 사용하세요.";
        UpdateBlankSessionHint();
        _ = SearchAsync();
        _ = RefreshKeywordsAsync();
    }
    private void RestoreFilters()
    {
        if (projection is null) return;
        suppressFilters = true;
        var selected = contextActive && normalSelectedThreads is not null ? normalSelectedThreads : projection.SelectedThreads;
        foreach (var item in threadItems) item.IsSelected = selected.Contains(item.Id);
        suppressFilters = false;
    }
    private async void Thread_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!suppressFilters && e.PropertyName == nameof(ThreadItem.IsSelected)) await FilterAsync();
    }
    private async Task FilterAsync(EntryFilter? requestedFilter = null, PositionAnchor? returnAnchor = null)
    {
        if (data is null || IsBlankSession) return;
        var captured = data;
        var capturedScope = ActiveScope;
        var selected = threadItems.Where(t => t.IsSelected).Select(t => t.Id).ToArray();
        var filter = requestedFilter ?? appliedFilter;
        var op = BeginWork("필터 적용 준비…", false);
        var timer = Stopwatch.StartNew();
        try
        {
            await Task.Delay(100, op.Token);
            var result = await Task.Run(() =>
            {
                var view = LogProjection.CreateFiltered(captured, selected, filter, op.Token, op.Progress, capturedScope);
                return (view, document: PrepareDocument(view.Text, op.Token, op.Progress));
            }, op.Token);
            op.Token.ThrowIfCancellationRequested();
            if (!work.IsCurrent(op.Version)) return;
            appliedFilter = filter;
            contextActive = false;
            margin.ContextLineIndex = null;
            threadRenderer.ContextEntryIndex = null;
            ContextPanel.Visibility = Visibility.Collapsed;
            UpdateExportLabel(false);
            PublishView(result.view, result.document);
            if (returnAnchor is not null) RestorePosition(returnAnchor);
            UpdateFilterSummary();
            OperationStatus.Text = $"필터 적용 완료 · {timer.Elapsed.TotalSeconds:F2}초 · 원본 기록 순서" +
                (positionMovedToNearest ? " · 이전 위치가 숨겨져 가까운 원본 줄로 이동" : "");
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) { RestoreFilters(); OperationStatus.Text = "필터 취소 · 이전 화면 유지"; } }
        catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException)
        { if (work.IsCurrent(op.Version)) { RestoreFilters(); ShowError("필터 적용 실패", ex); } }
        finally { FinishWork(op.Version); }
    }
    private async Task SetThreadsAsync(Func<ThreadItem, bool> predicate)
    {
        suppressFilters = true;
        foreach (var item in threadItems) item.IsSelected = predicate(item);
        suppressFilters = false;
        await FilterAsync();
    }
    private async void SelectAll_Click(object sender, RoutedEventArgs e) => await SetThreadsAsync(_ => true);
    private async void SelectNone_Click(object sender, RoutedEventArgs e) => await SetThreadsAsync(_ => false);
    private async void Only_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is ThreadItem item) await SetThreadsAsync(t => t.Id == item.Id);
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { fileBatchVersion++; work.Cancel(); searchWork.Cancel(); OperationStatus.Text = "취소 요청됨 · 현재 처리 단계가 끝나면 중단합니다."; }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (projection is null || busy || IsBlankSession) return;
        var captured = projection;
        string exportScope = contextActive ? "주변 로그" : "필터 결과";
        string exportName = captured.Source.SourcePath is null ? "pasted-log" : Path.GetFileNameWithoutExtension(captured.Source.SourcePath);
        var dialog = new SaveFileDialog { Title = exportScope + " 내보내기 — UTF-8 (BOM 없음), 새 파일만", Filter = "로그 파일 (*.log)|*.log|텍스트 파일 (*.txt)|*.txt", FileName = exportName + (contextActive ? "-context.log" : "-filtered.log"), OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return;
        var op = BeginWork(exportScope + " 내보내기…", true);
        try
        {
            await Task.Run(() => LogExporter.ExportAsync(captured, dialog.FileName, op.Token, op.Progress), op.Token);
            if (work.IsCurrent(op.Version)) OperationStatus.Text = $"내보내기 완료 · {LogExporter.EncodingDescription} · {dialog.FileName}";
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) OperationStatus.Text = "내보내기 취소"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or EncoderFallbackException)
        { if (work.IsCurrent(op.Version)) ShowError("내보내기 실패", ex); }
        finally { FinishWork(op.Version); }
    }
    private void ShowError(string title, Exception ex)
    {
        OperationStatus.Text = $"{title} · 이전 화면 유지";
        if (IsVisible) MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Wrap_Changed(object sender, RoutedEventArgs e) { if (Editor is not null) Editor.WordWrap = WrapBox.IsChecked == true; ScheduleSettingsSave(); }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e) { if (viewReady) { ApplyTheme(); ScheduleSettingsSave(); } }
    private void ApplyTheme()
    {
        theme = new(ThemeBox.SelectedIndex != 1);
        theme.Apply(Resources);
        if (Application.Current is { } application) theme.Apply(application.Resources);
        theme.ApplyTitleBar(this);
        threadRenderer.Theme = margin.Theme = searchRenderer.Theme = theme;
        keywordRenderer.IsDark = theme.IsDark;
        foreach (var item in threadItems) item.ApplyTheme(theme);
        Editor.TextArea.SelectionBrush = theme.Selection;
        Editor.TextArea.SelectionForeground = theme.SelectionText;
        Editor.TextArea.SelectionBorder = new Pen(theme.Selection, 1);
        Editor.TextArea.Caret.CaretBrush = theme.Text;
        margin.InvalidateVisual();
        Editor.TextArea.TextView.Redraw();
    }
    private void Density_Changed(object sender, SelectionChangedEventArgs e) { if (viewReady) { ApplyTypography(); ScheduleSettingsSave(); } }
    private void ApplyTypography()
    {
        Editor.FontFamily = LogTypography.Create(DensityBox.SelectedIndex == 1);
        margin.LogFontFamily = Editor.FontFamily;
        margin.InvalidateMeasure(); margin.InvalidateVisual();
        Editor.TextArea.TextView.Redraw();
    }
    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Editor is null || FontSizeBox.SelectedItem is not ComboBoxItem item) return;
        Editor.FontSize = double.Parse(item.Content.ToString()!, CultureInfo.InvariantCulture);
        margin.LogFontSize = Editor.FontSize;
        margin.InvalidateMeasure(); margin.InvalidateVisual();
        ScheduleSettingsSave();
    }
}
