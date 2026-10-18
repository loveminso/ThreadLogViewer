using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private SettingsStore? settingsStore;
    private bool settingsReady, saveSettings, settingsDirty;
    private int settingsSaveFailures;
    private readonly DispatcherTimer settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private System.ComponentModel.DependencyPropertyDescriptor? widthDescriptor;
    private EventHandler? widthChanged;
    private void InitializeFeatures(string? directory, bool persist)
    {
        saveSettings = persist;
        settingsStore = directory is null
            ? new SettingsStore(AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            : new SettingsStore(directory);
        var loaded = settingsStore.Load();
        saveSettings &= loaded.CanSave;
        var settings = loaded.Settings.Normalize();
        ThemeBox.SelectedIndex = settings.Dark ? 0 : 1;
        DensityBox.SelectedIndex = settings.Compact ? 1 : 0;
        FontSizeBox.SelectedIndex = Array.IndexOf(new double[] { 11, 12, 13, 14, 16, 18, 20, 24 }, settings.FontSize);
        WrapBox.IsChecked = settings.WordWrap;
        ThreadColumn.Width = new GridLength(settings.PanelWidth);
        InitializeResultsLayout(settings.ResultsHeight, settings.ResultsCollapsed);
        threadRenderer.Enabled = settings.ThreadBackgrounds;
        ThreadBackgroundMenu.IsChecked = settings.ThreadBackgrounds;
        largeLineGenerator.Enabled = settings.LargeLinePreview;
        LargeLineMenu.IsChecked = settings.LargeLinePreview;
        KeywordList.ItemsSource = keywordRules;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdatePosition();
        Editor.TextArea.SelectionChanged += (_, _) => UpdatePosition();
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (projection?.Count > 0 && !restoringPosition) emptyAnchor = null; };
        SizeChanged += (_, _) => { if (viewReady) { UpdateLayoutLimits(); ScheduleSettingsSave(); } };
        // GridSplitter width changes do not always raise Window.SizeChanged.
        widthDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition));
        widthChanged = (_, _) => ScheduleSettingsSave();
        widthDescriptor.AddValueChanged(ThreadColumn, widthChanged);
        settingsTimer.Tick += SettingsTimer_Tick;
        settingsReady = true;
        if (!loaded.Success) OperationStatus.Text = "화면 설정 · " + loaded.Message;
    }
    private void UpdateLayoutLimits()
    {
        double height = ActualHeight > 0 ? ActualHeight : Height;
        double contextSpace = ContextPanel.Visibility == Visibility.Visible ? 50 : 0;
        ToolsScroll.MaxHeight = Math.Clamp(height - 600 - contextSpace, contextSpace > 0 ? 80 : 130, 230);
        UpdateResultsLayout();
    }
    private void ScheduleSettingsSave()
    {
        if (!settingsReady || !viewReady || !saveSettings) return;
        settingsDirty = true; settingsSaveFailures = 0; settingsTimer.Stop();
        settingsTimer.Interval = TimeSpan.FromMilliseconds(500); settingsTimer.Start();
    }
    private void SettingsTimer_Tick(object? sender, EventArgs e) => SaveSettingsNow();
    private void SaveSettingsNow()
    {
        settingsTimer.Stop();
        if (!settingsDirty || !saveSettings || settingsStore is null) return;
        var settings = new UiSettings
        {
            Dark = ThemeBox.SelectedIndex != 1, Compact = DensityBox.SelectedIndex == 1,
            WordWrap = WrapBox.IsChecked == true, FontSize = Editor.FontSize,
            // A narrow window only clamps the column. The absolute GridLength is the preference.
            PanelWidth = ThreadColumn.Width.IsAbsolute ? ThreadColumn.Width.Value : ThreadColumn.ActualWidth,
            ResultsHeight = preferredResultsHeight, ResultsCollapsed = resultsCollapsed,
            ThreadBackgrounds = threadRenderer.Enabled, LargeLinePreview = largeLineGenerator.Enabled
        };
        SettingsSaveResult result;
        try { result = settingsStore.Save(settings.Normalize(), data?.SourcePath); }
        catch (Exception) { result = new(false, "설정을 저장하지 못했습니다. 현재 화면 설정은 유지됩니다."); }
        if (result.Success)
        { settingsDirty = false; settingsSaveFailures = 0; settingsTimer.Interval = TimeSpan.FromMilliseconds(500); }
        else
        {
            string detail = result.Message.Contains('\\') || result.Message.Contains('/')
                ? "설정을 저장하지 못했습니다. 현재 화면 설정은 유지됩니다."
                : result.Message;
            OperationStatus.Text = "화면 설정 저장 안 됨 · " + detail + " · 파일 잠금과 폴더 권한을 확인하세요. 종료 시 다시 시도합니다.";
            settingsSaveFailures++;
            if (viewReady && settingsSaveFailures <= 3)
            { settingsTimer.Interval = TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, settingsSaveFailures))); settingsTimer.Start(); }
        }
    }
    private void ResetDocumentFeatures()
    {
        ResetLineSelectionGesture();
        separationStartLine = null; UpdateSplitStatus();
        timeA = timeB = null; UpdateTime();
        contextActive = false; normalSelectedThreads = null; normalAnchor = emptyAnchor = null; margin.ContextLineIndex = null;
        threadRenderer.ContextEntryIndex = null;
        hiddenTargetLine = null; HiddenContextButton.Visibility = Visibility.Collapsed;
        ContextPanel.Visibility = Visibility.Collapsed; fixedSearchRanges = null; lastSearchLocation = null;
        appliedFilter = EntryFilter.Empty; IncludeBox.Clear(); ExcludeBox.Clear();
        IncludeModeBox.SelectedIndex = 0; FilterCaseBox.IsChecked = false; UpdateFilterSummary();
        UpdateExportLabel(false);
    }
    private void DisposeFeatures()
    {
        ResetLineSelectionGesture();
        SaveSettingsNow(); settingsTimer.Stop();
        settingsTimer.Tick -= SettingsTimer_Tick;
        DisposeResultsLayout();
        DisposeWindowLayout(); DisposeEditing();
        if (widthChanged is not null) widthDescriptor?.RemoveValueChanged(ThreadColumn, widthChanged);
        highlightWork.Dispose(); selectionWork.Dispose();
    }
    private void Help_Click(object sender, RoutedEventArgs e) => HelpWindow.Show(this);
}
