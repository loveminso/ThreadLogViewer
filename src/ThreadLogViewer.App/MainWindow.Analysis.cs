using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Rendering;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private readonly KeywordHighlightRenderer keywordRenderer = new();
    private readonly LatestOperation highlightWork = new();
    private readonly ObservableCollection<KeywordRuleItem> keywordRules = [];
    private TimeAnchor? timeA, timeB;
    private void Analysis_Click(object sender, RoutedEventArgs e) => ShowHighlightPrompt();
    private void TimeA_Click(object sender, RoutedEventArgs e) => SetTimePoint(true);
    private void TimeB_Click(object sender, RoutedEventArgs e) => SetTimePoint(false);
    private void SetTimePoint(bool isA)
    {
        if (data is null || LineActionSourceLine() is not { } line) return;
        var point = LogTimeAnalysis.ResolveAnchor(data, line);
        if (point is null)
        {
            string message = $"원본 {line + 1:N0}줄의 소유 헤더에 유효한 시간이 없습니다. 이전 시간 지정은 유지됩니다.";
            TimeDifferenceStatus.Text = OperationStatus.Text = message;
            return;
        }
        if (!isA && timeA is null)
        { OperationStatus.Text = "먼저 시간 값이 있는 줄을 우클릭하여 ‘시간 기준으로 지정’을 선택하세요."; return; }
        if (isA) timeA = point; else timeB = point;
        UpdateTime();
        OperationStatus.Text = isA ? $"원본 {line + 1:N0}줄을 시간 기준으로 지정했습니다. 비교할 줄을 우클릭하세요." : "시간 차이를 표시했습니다. 결과를 우클릭하면 복사하거나 지정한 줄로 이동할 수 있습니다.";
    }
    private void UpdateTime()
    {
        margin.TimeALineIndex = timeA?.SourceLineIndex; margin.TimeBLineIndex = timeB?.SourceLineIndex;
        TimeALabel.Content = TimeLabel("기준 (A)", timeA); TimeBLabel.Content = TimeLabel("비교 (B)", timeB);
        TimeDifferenceStatus.Text = timeA is null || timeB is null ? "시간 차이는 두 기록을 지정하면 표시됩니다." : "비교 (B) − 기준 (A) = " + LogTimeAnalysis.FormatTicks(LogTimeAnalysis.DifferenceTicks(timeA, timeB)) + (timeB.Ticks < timeA.Ticks ? " · 날짜 정보가 없어 자정 보정 안 함" : "");
        UpdateTimeResult();
        UpdateAnalysisInputState();
    }
    private void UpdateTimeResult()
    {
        if (TimeResultPanel is null || TimeSummary is null) return;
        TimeResultPanel.Visibility = timeA is null && timeB is null ? Visibility.Collapsed : Visibility.Visible;
        if (timeA is null) { TimeSummary.Text = ""; return; }
        string basis = $"기준: 원본 {timeA.SourceLineIndex + 1:N0}줄 ({timeA.TimestampText})";
        TimeSummary.Text = timeB is null ? basis + " · 비교할 줄을 우클릭하여 시간 차이를 확인하세요." :
            $"{basis} → 비교: 원본 {timeB.SourceLineIndex + 1:N0}줄 ({timeB.TimestampText}) · B − A = {LogTimeAnalysis.FormatTicks(LogTimeAnalysis.DifferenceTicks(timeA, timeB))}" +
            (timeB.Ticks < timeA.Ticks ? " · 자정 보정 안 함" : "");
        TimeSummary.ToolTip = timeB is null ? TimeLabel("기준", timeA) : TimeLabel("기준", timeA) + "\n" + TimeLabel("비교", timeB) + "\n" + TimeDifferenceStatus.Text;
        if (TimeResultPanel.ContextMenu is { } menu)
            foreach (var item in LineMenuItems(menu))
                switch (item.Tag as string)
                {
                    case "anchor-a": item.IsEnabled = timeA is not null; break;
                    case "anchor-b": item.IsEnabled = timeB is not null; break;
                    case "time-pair": item.IsEnabled = timeA is not null && timeB is not null; break;
                    case "time-clear": item.IsEnabled = timeA is not null || timeB is not null; break;
                }
    }
    private string TimeLabel(string name, TimeAnchor? point) => point is null ? name + ": 아직 지정하지 않았습니다" :
        $"{name} 원본 {point.SourceLineIndex + 1:N0}줄 · 헤더 {point.HeaderLineIndex + 1:N0}줄 · {point.TimestampText} · {(data?.Entries[point.EntryIndex].ThreadId is { } id ? "스레드 " + id : "미분류")}{(projection?.FindDisplayLine(point.SourceLineIndex + 1) is null ? " · 숨김 (문맥에서 열기)" : "")}";
    private void SwapTime_Click(object sender, RoutedEventArgs e) { if (timeA is null || timeB is null) return; (timeA, timeB) = (timeB, timeA); UpdateTime(); }
    private void ClearTime_Click(object sender, RoutedEventArgs e) { timeA = timeB = null; UpdateTime(); }
    private async void GoTimeA_Click(object sender, RoutedEventArgs e) { if (timeA is not null) await NavigateOriginalAsync(timeA.SourceLineIndex); }
    private async void GoTimeB_Click(object sender, RoutedEventArgs e) { if (timeB is not null) await NavigateOriginalAsync(timeB.SourceLineIndex); }
    private void CopyTime_Click(object sender, RoutedEventArgs e)
    {
        if (timeA is null || timeB is null) return;
        UpdateTime();
        try { Clipboard.SetText($"{TimeLabel("A", timeA)}\n{TimeLabel("B", timeB)}\n{TimeDifferenceStatus.Text}"); }
        catch (System.Runtime.InteropServices.ExternalException) { TimeDifferenceStatus.Text = OperationStatus.Text = "클립보드 사용 중 · 다시 복사하세요."; }
    }
    private sealed class KeywordRuleItem(string keyword, int colorIndex) : INotifyPropertyChanged
    {
        private static readonly Brush[] Palette = new[] { "#F7A844", "#B397FF", "#4BD6CC", "#75AEFF", "#FF92BD", "#97D778" }
            .Select(value => { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); brush.Freeze(); return (Brush)brush; }).ToArray();
        public string Keyword { get; } = keyword;
        public int ColorIndex { get; } = colorIndex;
        public Brush Color => Palette[ColorIndex];
        public string Label => $"{new[] { "주황", "보라", "청록", "파랑", "분홍", "초록" }[ColorIndex]} · {Keyword}";
        private bool enabled = true;
        public bool Enabled { get => enabled; set { if (enabled == value) return; enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    private void AddKeyword_Click(object sender, RoutedEventArgs e)
    {
        if (HighlightPrompt.PhraseError(KeywordBox.Text) is { } error) { KeywordStatus.Text = OperationStatus.Text = error; return; }
        if (keywordRules.Count >= HighlightPrompt.MaximumRules) { KeywordStatus.Text = OperationStatus.Text = "강조 규칙은 최대 8개입니다. ‘강조 문구 관리’에서 기존 문구를 수정하거나 삭제하세요. 사용을 끄는 것만으로는 빈자리가 생기지 않습니다."; return; }
        var rule = new KeywordRuleItem(KeywordBox.Text, Math.Clamp(KeywordColorBox.SelectedIndex, 0, 5));
        rule.PropertyChanged += Keyword_Changed; keywordRules.Add(rule); KeywordBox.Clear(); _ = RefreshKeywordsAsync();
    }
    private void RemoveKeyword_Click(object sender, RoutedEventArgs e)
    { if ((sender as Button)?.Tag is KeywordRuleItem rule) { rule.PropertyChanged -= Keyword_Changed; keywordRules.Remove(rule); _ = RefreshKeywordsAsync(); } }
    private void Keyword_Changed(object? sender, PropertyChangedEventArgs e) => _ = RefreshKeywordsAsync();
    private async Task RefreshKeywordsAsync()
    {
        if (!viewReady) return;
        var op = highlightWork.Begin(); var captured = projection;
        keywordRenderer.Index = HighlightIndex.Empty;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        UpdateAnalysisInputState();
        if (captured is null || keywordRules.Count == 0) { KeywordStatus.Text = $"{keywordRules.Count}/8 규칙"; return; }
        var rules = keywordRules.Where(r => r.Enabled).Select(r => (r.Keyword, r.ColorIndex)).ToArray();
        var scope = ActiveScope;
        IReadOnlyList<SourceTextRange>? ranges = scope is null ? null : [LogSlice.GetTextRange(captured.Source, scope)];
        KeywordStatus.Text = "강조 준비 중…";
        try
        {
            var result = await Task.Run(() =>
            {
                var spans = new List<HighlightSpan>(); bool limited = false;
                for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    var rule = rules[ruleIndex];
                    var found = LogSearch.Find(captured.Source, captured.EntryIndexes, rule.Keyword, new SearchOptions(), ranges, cancellationToken: op.Token);
                    limited |= found.Limited;
                    foreach (var hit in found.Hits)
                    {
                        op.Token.ThrowIfCancellationRequested();
                        if (scope is { } boundary && !boundary.Contains(hit.SourceLineIndex)) continue;
                        if (spans.Count == LogSearch.MaxHighlights) { limited = true; break; }
                        if (captured.GetDisplayOffset(hit.SourceOffset) is { } offset && hit.Length <= captured.Text.Length - offset)
                            spans.Add(new(offset, hit.Length, ruleIndex));
                    }
                    if (limited && spans.Count == LogSearch.MaxHighlights) break;
                }
                return (Index: new HighlightIndex(spans), Limited: limited);
            }, op.Token);
            if (!highlightWork.IsCurrent(op.Version) || captured != projection) return;
            keywordRenderer.RuleColors = rules.Select(r => r.ColorIndex).ToArray();
            keywordRenderer.Index = result.Index;
            KeywordStatus.Text = $"{keywordRules.Count}/8 규칙 · {result.Index.Count:N0}곳{(result.Limited ? " (첫 100,000곳)" : "")}";
            if (result.Limited) OperationStatus.Text = "문구 강조 제한 · 첫 100,000곳만 표시합니다. 강조 문구나 필터 범위를 좁혀 주세요.";
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
        catch (OperationCanceledException) { }
        catch (OutOfMemoryException) { if (highlightWork.IsCurrent(op.Version)) KeywordStatus.Text = OperationStatus.Text = "강조 메모리 부족 · 규칙을 줄여 주세요."; }
    }
}
