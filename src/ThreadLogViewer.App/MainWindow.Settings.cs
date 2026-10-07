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
        settingsStore = new SettingsStore(directory ?? AppContext.BaseDirectory);
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
            PanelWidth = ThreadColumn.ActualWidth > 0 ? ThreadColumn.ActualWidth : ThreadColumn.Width.Value,
            ResultsHeight = preferredResultsHeight, ResultsCollapsed = resultsCollapsed,
            ThreadBackgrounds = threadRenderer.Enabled, LargeLinePreview = largeLineGenerator.Enabled
        };
        var result = settingsStore.Save(settings.Normalize(), data?.SourcePath);
        if (result.Success)
        { settingsDirty = false; settingsSaveFailures = 0; settingsTimer.Interval = TimeSpan.FromMilliseconds(500); }
        else
        {
            OperationStatus.Text = "화면 설정 저장 안 됨 · " + result.Message + " · 파일 잠금과 폴더 권한을 확인하세요. 종료 시 다시 시도합니다.";
            settingsSaveFailures++;
            if (viewReady && settingsSaveFailures <= 3)
            { settingsTimer.Interval = TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, settingsSaveFailures))); settingsTimer.Start(); }
        }
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
        settingsTimer.Tick -= SettingsTimer_Tick;
        DisposeResultsLayout();
        DisposeWindowLayout(); DisposeEditing();
        if (widthChanged is not null) widthDescriptor?.RemoveValueChanged(ThreadColumn, widthChanged);
        highlightWork.Dispose(); selectionWork.Dispose();
    }
    private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Ctrl+N   새 빈 탭 만들기\nCtrl+O   여러 로그 파일 열기\nCtrl+V   복사한 로그 열기 (입력 칸에서는 텍스트 붙여넣기)\nCtrl+Shift+V   어디서든 클립보드 로그 열기\nCtrl+Tab / Ctrl+Shift+Tab   다음 / 이전 탭\nCtrl+W   현재 탭 닫기\nCtrl+Shift+T   최근 닫은 탭 복원\nF5   상태를 유지하며 파일 새로 고침\nAlt+← / Alt+→   탐색 뒤로 / 앞으로\nCtrl+C / Ctrl+A   활성 입력칸 또는 본문 복사 / 전체 선택\nCtrl+F   검색\nF3 / F4   다음 / 이전 검색 결과 (Shift+F3도 이전)\nShift+F8   선택 문구 / 커서 단어 강조 추가·해제\nCtrl+G   원본 줄 이동\nCtrl+F2   현재 원본 줄 북마크 표시 / 해제\nF2 / Shift+F2   다음 / 이전 북마크\nCtrl++ / Ctrl+- / Ctrl+0   글자 크게 / 작게 / 기본 크기\nEsc   줄 이동 또는 검색 닫기\nF1   이 도움말\n\n탭 오른쪽 + 버튼 또는 파일 → 새 탭으로 빈 탭을 만드세요. 빈 탭에서 파일을 열거나 로그를 붙여넣으면 그 탭을 채웁니다.\n파일 · 편집 · 보기 · 분석 메뉴에서 기능을 선택하세요. 메뉴 오른쪽에 단축키가 표시됩니다.\n인코딩은 자동으로 읽습니다. 필요한 경우 파일 → 인코딩으로 다시 읽기 → 한국어 (CP949)를 사용하세요.\n줄 번호 클릭은 줄 전체 선택, 줄 번호에서 드래그하면 여러 줄 선택입니다. Ctrl+클릭으로 떨어진 줄을 추가·해제하고 Ctrl+C로 함께 복사합니다.\nShift+F8은 선택한 문구 또는 커서 아래 단어를 바로 강조합니다. 새 문구에는 다른 색을 배정하고, 같은 문구에 다시 누르면 강조를 해제합니다.\n강조 문구 관리는 분석 메뉴에서 열 수 있습니다. 추가 버튼으로 새 문구를 등록하고 적용 버튼으로 등록된 문구·색·켜기 변경을 반영합니다.\n보기 → 스레드별 배경색으로 행 배경을 켜고 끌 수 있습니다. 스레드 목록 위 찾기는 본문 필터를 바꾸지 않습니다.\n닫은 탭은 실행 중 최대 8개·추가 보관 추정 128 MiB 안에서 복원할 수 있습니다. 재실행 후에는 복원되지 않습니다.\nF5는 파일 탭에서 조건·강조를 유지합니다. 삭제·중복되어 확인할 수 없는 북마크와 위치는 제거하거나 가까운 위치로 복원하고 결과를 안내합니다.\n로그 줄을 우클릭하면 대상 줄의 배경·테두리와 줄 번호를 표시하며 세션 분리 시작·끝, 주변 로그, 시간 비교를 사용할 수 있습니다.\n시간 비교는 기준 줄 지정 → 다른 줄에서 기준과 이 줄 시간 차이를 선택합니다.\n파일 → 분석 상태 저장/불러오기로 원문·필터·강조·북마크·위치·탐색 이력을 다시 사용할 수 있습니다. 새 파일만 만들며 붙여넣기는 원문 포함 총 8 MiB까지 저장합니다. 큰 붙여넣기는 먼저 로그로 내보내세요.\n분석 → 필터·강조 프리셋으로 현재 적용된 조건과 강조를 다른 로그에도 적용하세요.\n보기 → 긴 줄 나눠보기는 매우 긴 한 줄을 구간별로 보여줍니다. 앞/뒤 표시를 클릭해 이동하며 검색·복사·내보내기는 전체 원문을 유지합니다. 선택은 화면 설정에 저장됩니다.\n단축키 사용자 지정은 후속 버전에 추가할 예정입니다.",
        "ThreadLog Viewer 단축키", MessageBoxButton.OK, MessageBoxImage.Information);
}
