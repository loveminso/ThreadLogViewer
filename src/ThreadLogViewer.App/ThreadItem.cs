using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public sealed class ThreadItem(ThreadSummary summary) : INotifyPropertyChanged
{
    public int? Id => summary.ThreadId;
    public string Label => Id is null ? "미분류" : $"T {Id}";
    public string CountLabel => $"{summary.Count:N0}줄";
    public string Times => $"최초 {summary.FirstRecordedTime ?? "시각 없음"}\n마지막 {summary.LastRecordedTime ?? "시각 없음"}";
    public Brush Color { get; } = (Brush)new BrushConverter().ConvertFromString(ThreadColors.ForThread(summary.ThreadId))!;
    private bool selected = true;
    public bool IsSelected { get => selected; set { if (selected == value) return; selected = value; OnPropertyChanged(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
