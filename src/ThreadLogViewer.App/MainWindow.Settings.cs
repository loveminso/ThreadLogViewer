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
    private readonly DispatcherTimer settingsTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private System.ComponentModel.DependencyPropertyDescriptor? widthDescriptor;
    private EventHandler? widthChanged;
    private void InitializeFeatures(string? directory, bool persist)
    {
        saveSettings = persist;
        settingsStore = new SettingsStore(directory ?? AppContext.BaseDirectory);
        var loaded = settingsStore.Load();
        saveSettings &= loaded.CanSave;
        var settings = loaded.Settings.Normalize();
        ThemeBox.SelectedIndex = settings.Dark ? 0 : 1;
        DensityBox.SelectedIndex = settings.Compact ? 1 : 0;
        FontSizeBox.SelectedIndex = Array.IndexOf(new double[] { 11, 12, 13, 14, 16, 18, 20, 24 }, settings.FontSize);
        WrapBox.IsChecked = settings.WordWrap;
        ThreadColumn.Width = new GridLength(settings.PanelWidth);
        KeywordList.ItemsSource = keywordRules;
        Editor.TextArea.Caret.PositionChanged += (_, _) => UpdatePosition();
        Editor.TextArea.SelectionChanged += (_, _) => UpdatePosition();
        Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (projection?.Count > 0 && !restoringPosition) emptyAnchor = null; };
        SizeChanged += (_, _) => { if (viewReady) { UpdateLayoutLimits(); ScheduleSettingsSave(); } };
        // GridSplitter width changes do not always raise Window.SizeChanged.
        widthDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition));
        widthChanged = (_, _) => ScheduleSettingsSave();
        widthDescriptor.AddValueChanged(ThreadColumn, widthChanged);
        settingsTimer.Tick += (_, _) => SaveSettingsNow();
        settingsReady = true;
        if (!loaded.Success) OperationStatus.Text = "화면 설정 · " + loaded.Message;
    }
    private void UpdateLayoutLimits()
    {
        double height = ActualHeight > 0 ? ActualHeight : Height;
        double contextSpace = ContextPanel.Visibility == Visibility.Visible ? 50 : 0;
        ToolsScroll.MaxHeight = Math.Clamp(height - 600 - contextSpace, contextSpace > 0 ? 80 : 130, 230);
        ResultsList.Height = Math.Clamp(height - 650, 70, 135);
    }
    private void ScheduleSettingsSave()
    {
        if (!settingsReady || !viewReady || !saveSettings) return;
        settingsDirty = true; settingsTimer.Stop(); settingsTimer.Start();
    }
    private void SaveSettingsNow()
    {
        settingsTimer.Stop();
        if (!settingsDirty || !saveSettings || settingsStore is null) return;
        settingsDirty = false;
        var settings = new UiSettings
        {
            Dark = ThemeBox.SelectedIndex != 1, Compact = DensityBox.SelectedIndex == 1,
            WordWrap = WrapBox.IsChecked == true, FontSize = Editor.FontSize,
            PanelWidth = ThreadColumn.ActualWidth > 0 ? ThreadColumn.ActualWidth : ThreadColumn.Width.Value
        };
        var result = settingsStore.Save(settings.Normalize(), data?.SourcePath);
        if (!result.Success) OperationStatus.Text = "화면 설정 저장 안 됨 · " + result.Message;
    }
    private void ResetDocumentFeatures()
    {
        ResetLineSelectionGesture();
        separationStartLine = null; UpdateSplitStatus();
        bookmarks.Clear(); RefreshBookmarks(); timeA = timeB = null; UpdateTime();
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
        if (widthChanged is not null) widthDescriptor?.RemoveValueChanged(ThreadColumn, widthChanged);
        highlightWork.Dispose(); selectionWork.Dispose();
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Ctrl+O   여러 로그 파일 열기\nCtrl+V   복사한 로그 새 탭으로 열기 (입력 칸에서는 텍스트 붙여넣기)\nCtrl+Shift+V   어디서든 클립보드 로그 새 탭으로 열기\nCtrl+Tab / Ctrl+Shift+Tab   다음 / 이전 탭\nCtrl+W   현재 탭 닫기\nCtrl+C / Ctrl+A   본문 복사 / 전체 선택\nCtrl+F   검색\nF3 / F4   다음 / 이전 검색 결과 (Shift+F3도 이전)\nShift+F8   선택 문구 / 커서 단어 강조 추가·해제\nCtrl+G   원본 줄 이동\nCtrl+F2   현재 원본 줄 북마크 표시 / 해제\nF2 / Shift+F2   다음 / 이전 북마크\nCtrl++ / Ctrl+- / Ctrl+0   글자 크게 / 작게 / 기본 크기\nEsc   줄 이동 또는 검색 닫기\nF1   이 도움말\n\n파일 · 편집 · 보기 · 분석 메뉴에서 기능을 선택하세요. 메뉴 오른쪽에 단축키가 표시됩니다.\n인코딩은 자동으로 읽습니다. 필요한 경우 파일 → 인코딩으로 다시 읽기 → 한국어 (CP949)를 사용하세요.\n줄 번호 클릭은 줄 전체 선택, 줄 번호에서 드래그하면 여러 줄 선택입니다. Ctrl+클릭으로 떨어진 줄을 추가·해제하고 Ctrl+C로 함께 복사합니다.\nShift+F8은 선택한 문구 또는 커서 아래 단어를 바로 강조합니다. 새 문구에는 다른 색을 배정하고, 같은 문구에 다시 누르면 강조를 해제합니다.\n강조 문구 관리는 분석 메뉴에서 열 수 있습니다.\n로그 줄을 우클릭하면 대상 줄의 배경·테두리와 줄 번호를 표시하며 세션 분리 시작·끝, 주변 로그, 시간 비교를 사용할 수 있습니다.\n시간 비교는 기준 줄 지정 → 다른 줄에서 기준과 이 줄 시간 차이를 선택합니다.\n단축키 사용자 지정은 후속 버전에 추가할 예정입니다.",
        "ThreadLog Viewer 단축키", MessageBoxButton.OK, MessageBoxImage.Information);
}
