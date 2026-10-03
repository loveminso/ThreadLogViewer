using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    public static readonly DependencyProperty CanChangeFiltersProperty = DependencyProperty.Register(
        nameof(CanChangeFilters), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool CanChangeFilters { get => (bool)GetValue(CanChangeFiltersProperty); private set => SetValue(CanChangeFiltersProperty, value); }
    private bool choosingAnalysisTool, filtersLocked;
    private void InitializeAnalysis()
    {
        SetAnalysisTool(0, false);
        UpdateExportLabel(false);
        UpdateAnalysisInputState();
        UpdateFilterDraftStatus();
    }
    private void ShowAnalysisTool(int index) => SetAnalysisTool(index, false);
    private void SetAnalysisTool(int index, bool expand)
    {
        index = Math.Clamp(index, 0, 2);
        choosingAnalysisTool = true;
        ContextToolButton.IsChecked = index == 0;
        TimeToolButton.IsChecked = index == 1;
        HighlightToolButton.IsChecked = index == 2;
        choosingAnalysisTool = false;
        ContextToolPanel.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TimeToolPanel.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        HighlightToolPanel.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        AnalysisPanel.IsExpanded = false;
        UpdateAnalysisInputState();
    }
    private void AnalysisTool_Changed(object sender, RoutedEventArgs e)
    {
        if (!viewReady || choosingAnalysisTool || sender is not RadioButton { Tag: string tag }) return;
        if (int.TryParse(tag, out int index)) ShowAnalysisTool(index);
    }
    private void SetFilterLock(bool locked)
    { filtersLocked = locked; UpdateAnalysisInputState(); }
    private void UpdateAnalysisInputState()
    {
        if (CurrentRecordStatus is null || ContextOpenButton is null) return;
        CanChangeFilters = data is not null && !IsBlankSession && !contextActive && !filtersLocked;
        FilterModeHint.Visibility = contextActive ? Visibility.Visible : Visibility.Collapsed;
        FilterModeHint.Text = "주변 보기에서는 스레드·내용 필터를 잠시 적용하지 않습니다. 북마크는 사용할 수 있습니다.";
        int? line = CurrentSourceLine();
        if (projection?.Source != data) line = null;
        TimeAnchor? point = null;
        if (data is not null && line is { } sourceLine)
        {
            var entry = data.Entries[data.Lines[sourceLine].EntryIndex];
            point = LogTimeAnalysis.ResolveAnchor(data, sourceLine);
            string owner = entry.ThreadId is { } id ? $"T {id}" : "미분류";
            string stamp = point?.TimestampText ?? "유효 시각 없음";
            CurrentRecordStatus.Text = $"현재 선택: 원본 {sourceLine + 1:N0}줄 · 헤더 {entry.StartLineIndex + 1:N0}줄 · {owner} · {stamp}";
        }
        else CurrentRecordStatus.Text = data is null ? "로그를 연 뒤 분석할 기록을 클릭하세요." : "이 위치에는 원본 기록이 없습니다. 로그 본문을 클릭하세요.";
        ContextOpenButton.IsEnabled = !busy && line is not null;
        TimeAButton.IsEnabled = TimeBButton.IsEnabled = !busy && point is not null;
        TimeALabel.IsEnabled = !busy && timeA is not null;
        TimeBLabel.IsEnabled = !busy && timeB is not null;
        SwapTimeButton.IsEnabled = CopyTimeButton.IsEnabled = !busy && timeA is not null && timeB is not null;
        ClearTimeButton.IsEnabled = !busy && (timeA is not null || timeB is not null);
        TimeInstruction.Text = point is null ? "시간 값이 있는 로그 기록을 클릭하세요. 본문 줄은 소유 헤더의 시간을 사용합니다." :
            timeA is null ? "로그에서 기준 기록을 클릭하고 지정한 뒤, 비교할 기록을 클릭해 지정하세요." :
            timeB is null ? "기준이 지정되었습니다. 비교할 기록을 클릭하고 ‘현재 기록을 비교로 지정’을 누르세요." :
            "비교 (B) − 기준 (A)의 시간 차이입니다. 지정한 기록 버튼으로 해당 위치에 이동할 수 있습니다.";
        KeywordAddButton.IsEnabled = !busy && projection is not null && !IsBlankSession && KeywordBox.Text.Length > 0 && keywordRules.Count < 8;
        KeywordEmptyHint.Visibility = keywordRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateExportLabel(bool isContext)
    {
        string label = isContext ? "주변 로그 내보내기" : "필터 결과 내보내기";
        ExportLabel.Text = label;
        AutomationProperties.SetName(ExportButton, label);
        ExportButton.ToolTip = isContext ? "현재 주변 보기에 표시한 원본 줄을 새 UTF-8 파일로 내보내기" : "현재 필터에 표시한 원본 줄을 새 UTF-8 파일로 내보내기";
        UpdateLayoutLimits();
    }
    private void KeywordInput_Changed(object sender, TextChangedEventArgs e)
    { if (viewReady) UpdateAnalysisInputState(); }
    private void Keyword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        if (KeywordAddButton.IsEnabled) AddKeyword_Click(sender, new());
    }
}
