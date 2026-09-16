using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public sealed class ThreadItem(ThreadSummary summary, WorkbenchTheme theme) : INotifyPropertyChanged
{
    public int? Id => summary.ThreadId;
    public string Label => Id is null ? "미분류" : $"T {Id}";
    public string CountLabel => $"{summary.Count:N0}줄";
    public string Times => summary.FirstRecordedTime is null ? "시각 없음" : $"{summary.FirstRecordedTime} → {summary.LastRecordedTime}";
    public string DetailedTimes => $"최초 기록 {summary.FirstRecordedTime ?? "시각 없음"}\n마지막 기록 {summary.LastRecordedTime ?? "시각 없음"}\n파일 기록 순서 기준";
    public Brush Color { get; private set; } = theme.ThreadMarker(summary.ThreadId);
    public void ApplyTheme(WorkbenchTheme value) { Color = value.ThreadMarker(Id); OnPropertyChanged(nameof(Color)); }
    private bool selected = true;
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
