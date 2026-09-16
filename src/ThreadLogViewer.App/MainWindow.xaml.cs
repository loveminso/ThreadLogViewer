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
    private const string AppTitle = "ThreadLog Viewer v0.2.0";

    public MainWindow()
    {
        InitializeComponent();
        Editor.TextArea.AllowDrop = true;
        Editor.TextArea.LeftMargins.Add(margin);
        Editor.TextArea.TextView.BackgroundRenderers.Add(threadRenderer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(searchRenderer);
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = false;
        Editor.TextArea.SelectionCornerRadius = 0;
        viewReady = true;
        ApplyTheme();
        ApplyTypography();
        SourceInitialized += (_, _) => theme.ApplyTitleBar(this);
        Closed += (_, _) => { work.Dispose(); searchWork.Dispose(); };
        Loaded += async (_, _) =>
        {
            string? path = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault();
            if (path is not null) await OpenAsync(path, EncodingMode.Auto);
        };
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "로그 파일 열기", Filter = "로그 파일 (*.log;*.txt)|*.log;*.txt|모든 파일 (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await OpenAsync(dialog.FileName, EncodingMode.Auto);
    }
    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        if (requestedPath is not null) await OpenAsync(requestedPath, EncodingMode.Cp949);
    }
    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();
    private async Task PasteAsync()
    {
        try
        {
            var input = LogTransfer.ReadClipboard(Clipboard.GetDataObject());
            if (input.FilePath is not null) await OpenAsync(input.FilePath, EncodingMode.Auto);
            else await LoadAsync((token, progress) => Task.FromResult(LogParser.ParsePastedText(input.Text!, token, progress)),
                "붙여넣은 로그 준비…", "붙여넣은 로그를 열 수 없습니다");
        }
        catch (ExternalException) { ShowError("클립보드를 읽을 수 없습니다", new IOException("다른 프로그램이 클립보드를 사용 중입니다. 잠시 후 다시 붙여넣으세요.")); }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException)
        { ShowError("붙여넣기 실패", ex); }
    }
    private void Copy_Click(object sender, RoutedEventArgs e) => Editor.Copy();
    private void SelectText_Click(object sender, RoutedEventArgs e) { Editor.Focus(); Editor.SelectAll(); }
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; // Keep AvalonEdit's editing/drop handlers from consuming the file.
        e.Effects = DragDropEffects.None;
        try
        {
            if (e.AllowedEffects.HasFlag(DragDropEffects.Copy) && e.Data.GetDataPresent(DataFormats.FileDrop)
                && LogTransfer.IsSingleLogFile(e.Data.GetData(DataFormats.FileDrop) as string[]))
                e.Effects = DragDropEffects.Copy;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IOException) { }
    }
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!e.AllowedEffects.HasFlag(DragDropEffects.Copy)) return;
        try
        {
            string path = LogTransfer.ReadDroppedFile(e.Data);
            e.Effects = DragDropEffects.Copy; // Opening a file never moves or deletes the source.
            await OpenAsync(path, EncodingMode.Auto);
        }
        catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException)
        { ShowError("끌어 놓은 파일을 열 수 없습니다", ex); }
    }
    private (long Version, CancellationToken Token, IProgress<WorkProgress> Progress) BeginWork(string label, bool lockFilters)
    {
        var operation = work.Begin();
        busy = true;
        WorkPanel.Visibility = Visibility.Visible;
        ExportButton.IsEnabled = false;
        FilterPanel.IsEnabled = !lockFilters;
        CancelButton.Visibility = ProgressBar.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        OperationStatus.Text = label;
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
        FilterPanel.IsEnabled = true;
        ExportButton.IsEnabled = projection is not null;
        CancelButton.Visibility = ProgressBar.Visibility = Visibility.Collapsed;
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
    private Task OpenAsync(string path, EncodingMode mode)
    {
        requestedPath = path;
        ReloadButton.IsEnabled = true;
        return LoadAsync((token, progress) => LogFileReader.ReadAsync(path, mode, token, progress),
            "파일 읽기 준비…", "파일을 열 수 없습니다");
    }
    private async Task LoadAsync(Func<CancellationToken, IProgress<WorkProgress>, Task<LogData>> load,
        string startingMessage, string errorTitle)
    {
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
            if (!work.IsCurrent(op.Version)) return;
            data = result.loaded;
            foreach (var item in threadItems) item.PropertyChanged -= Thread_Changed;
            threadItems = data.Threads.Select(t => new ThreadItem(t, theme)).ToList();
            foreach (var item in threadItems) item.PropertyChanged += Thread_Changed;
            ThreadList.ItemsSource = threadItems;
            ThreadCount.Text = $"{threadItems.Count:N0}개";
            PublishView(result.view, result.document);
            requestedPath = data.SourcePath;
            ReloadButton.IsEnabled = requestedPath is not null;
            FileLabel.Text = data.SourcePath is null ? "붙여넣은 로그" : Path.GetFileName(data.SourcePath);
            FileLabel.ToolTip = data.SourcePath ?? "줄 번호는 붙여넣은 텍스트의 첫 줄부터 1입니다.";
            Title = $"{FileLabel.Text} — {AppTitle}";
            EncodingStatus.Text = data.EncodingDescription;
            ParseStatus.Text = $"파싱: 완전 {data.CompleteCount:N0} · 부분 {data.PartialCount:N0} · 미인식 {data.UnrecognizedCount:N0}";
            OperationStatus.Text = $"열기 완료 · {timer.Elapsed.TotalSeconds:F2}초 · 읽기 전용";
        }
        catch (OperationCanceledException) { if (work.IsCurrent(op.Version)) { RestoreFilters(); OperationStatus.Text = "열기 취소 · 이전 화면 유지"; } }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException or OutOfMemoryException)
        {
            if (work.IsCurrent(op.Version)) { RestoreFilters(); ShowError(errorTitle, ex); }
        }
        finally { FinishWork(op.Version); }
    }
    private void PublishView(LogProjection view, TextDocument document)
    {
        searchWork.Cancel();
        searchRenderer.Hits = [];
        projection = view;
        threadRenderer.Projection = margin.Projection = view;
        document.SetOwnerThread(Thread.CurrentThread);
        Editor.Document = document;
        margin.InvalidateMeasure(); margin.InvalidateVisual();
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        CountStatus.Text = $"표시 {view.Count:N0} / 전체 {view.Source.Lines.Count:N0}줄";
        EmptyHint.Text = view.Source.Lines.Count == 0 ? "빈 파일입니다." : "표시할 줄이 없습니다. 스레드 필터를 선택하세요.";
        EmptyPanel.Visibility = view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyDetail.Text = view.Source.Lines.Count == 0 ? "다른 파일을 열거나 Ctrl+V로 로그를 붙여넣으세요." : "전체 선택 또는 왼쪽 스레드 체크박스를 사용하세요.";
        _ = SearchAsync();
    }
    private void RestoreFilters()
    {
        if (projection is null) return;
        suppressFilters = true;
        foreach (var item in threadItems) item.IsSelected = projection.SelectedThreads.Contains(item.Id);
        suppressFilters = false;
    }
    private async void Thread_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (!suppressFilters && e.PropertyName == nameof(ThreadItem.IsSelected)) await FilterAsync();
    }
    private async Task FilterAsync()
    {
        if (data is null) return;
        var captured = data;
        var selected = threadItems.Where(t => t.IsSelected).Select(t => t.Id).ToArray();
        var op = BeginWork("필터 적용 준비…", false);
        var timer = Stopwatch.StartNew();
        try
        {
            await Task.Delay(100, op.Token);
            var result = await Task.Run(() =>
            {
                var view = LogProjection.Create(captured, selected, op.Token, op.Progress);
                return (view, document: PrepareDocument(view.Text, op.Token, op.Progress));
            }, op.Token);
            op.Token.ThrowIfCancellationRequested();
            if (!work.IsCurrent(op.Version)) return;
            PublishView(result.view, result.document);
            OperationStatus.Text = $"필터 적용 완료 · {timer.Elapsed.TotalSeconds:F2}초 · 원본 기록 순서";
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
    private void Cancel_Click(object sender, RoutedEventArgs e) { work.Cancel(); searchWork.Cancel(); OperationStatus.Text = "취소 요청됨 · 현재 처리 단계가 끝나면 중단합니다."; }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (projection is null || busy) return;
        var captured = projection;
        string exportName = captured.Source.SourcePath is null ? "pasted-log" : Path.GetFileNameWithoutExtension(captured.Source.SourcePath);
        var dialog = new SaveFileDialog { Title = "필터 결과 내보내기 — UTF-8 (BOM 없음), 새 파일만", Filter = "로그 파일 (*.log)|*.log|텍스트 파일 (*.txt)|*.txt", FileName = exportName + "-filtered.log", OverwritePrompt = false };
        if (dialog.ShowDialog(this) != true) return;
        var op = BeginWork("필터 결과 내보내기…", true);
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
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
    private void Wrap_Changed(object sender, RoutedEventArgs e) { if (Editor is not null) Editor.WordWrap = WrapBox.IsChecked == true; }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e) { if (viewReady) ApplyTheme(); }
    private void ApplyTheme()
    {
        theme = new(ThemeBox.SelectedIndex != 1);
        theme.Apply(Resources);
        if (Application.Current is { } application) theme.Apply(application.Resources);
        theme.ApplyTitleBar(this);
        threadRenderer.Theme = margin.Theme = searchRenderer.Theme = theme;
        foreach (var item in threadItems) item.ApplyTheme(theme);
        Editor.TextArea.SelectionBrush = theme.Selection;
        Editor.TextArea.SelectionForeground = theme.SelectionText;
        Editor.TextArea.SelectionBorder = new Pen(theme.Selection, 1);
        Editor.TextArea.Caret.CaretBrush = theme.Text;
        margin.InvalidateVisual();
        Editor.TextArea.TextView.Redraw();
    }
    private void Density_Changed(object sender, SelectionChangedEventArgs e) { if (viewReady) ApplyTypography(); }
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
    }
    private void ShowSearch() { SearchBar.Visibility = Visibility.Visible; SearchBox.Focus(); SearchBox.SelectAll(); _ = SearchAsync(); }
    private void Search_Click(object sender, RoutedEventArgs e) => ShowSearch();
    private void CloseSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBar.Visibility = Visibility.Collapsed; searchWork.Cancel(); searchRenderer.Hits = [];
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background); Editor.Focus();
    }
    private async void Search_Changed(object sender, TextChangedEventArgs e) => await SearchAsync();
    private async Task SearchAsync()
    {
        if (SearchBox is null || SearchStatus is null) return;
        var op = searchWork.Begin();
        searchRenderer.Hits = []; searchIndex = -1;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        string query = SearchBox.Text;
        var captured = projection;
        if (captured is null || query.Length == 0 || SearchBar.Visibility != Visibility.Visible) { SearchStatus.Text = ""; return; }
        SearchStatus.Text = "검색 중…";
        try
        {
            await Task.Delay(180, op.Token);
            var found = await Task.Run(() => LogSearch.Find(captured.Text, query, op.Token), op.Token);
            if (!searchWork.IsCurrent(op.Version) || op.Token.IsCancellationRequested || captured != projection) return;
            searchRenderer.Hits = found.Hits; searchLimited = found.Limited;
            SearchStatus.Text = found.Hits.Length == 0 ? "결과 없음" : $"{found.Hits.Length:N0}개{(found.Limited ? " (첫 100,000개만 표시)" : "")}";
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        catch (OperationCanceledException) { }
        catch (OutOfMemoryException) { if (searchWork.IsCurrent(op.Version)) SearchStatus.Text = "검색 메모리 부족 · 검색어를 좁혀 주세요."; }
    }
    private void NavigateSearch(bool backwards)
    {
        var hits = searchRenderer.Hits;
        if (hits.Length == 0) return;
        searchIndex = searchIndex < 0 ? (backwards ? hits.Length - 1 : 0) : (searchIndex + (backwards ? -1 : 1) + hits.Length) % hits.Length;
        var hit = hits[searchIndex];
        Editor.Select(hit.Offset, hit.Length);
        var location = Editor.Document.GetLocation(hit.Offset);
        Editor.ScrollTo(location.Line, location.Column);
        SearchStatus.Text = $"{searchIndex + 1:N0} / {hits.Length:N0}{(searchLimited ? " (첫 100,000개)" : "")}";
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => NavigateSearch(true);
    private void Next_Click(object sender, RoutedEventArgs e) => NavigateSearch(false);
    private void Search_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { NavigateSearch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; } }
    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (LogTransfer.OpensLogOnPaste(e.Key, Keyboard.Modifiers, Keyboard.FocusedElement)) { e.Handled = true; await PasteAsync(); }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.F) { ShowSearch(); e.Handled = true; }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.O) { Open_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.F3) { NavigateSearch(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)); e.Handled = true; }
        else if (e.Key == Key.Escape && SearchBar.Visibility == Visibility.Visible) { CloseSearch_Click(this, new()); e.Handled = true; }
    }
}
