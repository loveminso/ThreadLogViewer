using System.Windows;
using ICSharpCode.AvalonEdit.Rendering;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private readonly LargeLineElementGenerator largeLineGenerator = new();

    private void InitializePerformance()
    {
        Editor.TextArea.TextView.ElementGenerators.Insert(0, largeLineGenerator);
        largeLineGenerator.NavigateRequested += NavigateLargeLine;
        Editor.TextArea.Caret.PositionChanged += LargeLineCaret_Changed;
    }

    private void LargeLineCaret_Changed(object? sender, EventArgs e)
    {
        int previous = largeLineGenerator.FocusOffset;
        largeLineGenerator.FocusOffset = Editor.CaretOffset;
        if (!largeLineGenerator.Enabled || Editor.Document is not { } document) return;
        var line = document.GetLineByOffset(Editor.CaretOffset);
        if (line.Length > LargeLineElementGenerator.Threshold &&
            LargeLineElementGenerator.GetWindow(document, line, previous) != LargeLineElementGenerator.GetWindow(document, line, Editor.CaretOffset))
            Editor.TextArea.TextView.Redraw(line);
    }

    private void NavigateLargeLine(int offset)
    {
        using var navigation = BeginNavigation();
        Editor.CaretOffset = Math.Clamp(offset, 0, Editor.Document.TextLength);
        Editor.ScrollTo(Editor.TextArea.Caret.Line, Editor.TextArea.Caret.Column);
        Editor.Focus();
        OperationStatus.Text = "긴 줄 나눠보기 · 앞/뒤 표시를 클릭해 구간 이동 · 검색·복사·저장은 전체 원문 유지";
    }

    private void LargeLineMenu_Click(object sender, RoutedEventArgs e)
    {
        largeLineGenerator.Enabled = LargeLineMenu.IsChecked;
        largeLineGenerator.FocusOffset = Editor.CaretOffset;
        Editor.TextArea.TextView.Redraw();
        OperationStatus.Text = largeLineGenerator.Enabled
            ? "긴 줄 나눠보기 켜짐 · 앞/뒤 표시를 클릭해 이동 · 검색·복사·저장은 전체 원문 유지"
            : "긴 줄 전체 표시 · 매우 긴 한 줄은 표시와 줄 바꿈에 시간이 걸릴 수 있습니다";
        ScheduleSettingsSave();
    }

    private void DisposePerformance()
    {
        Editor.TextArea.Caret.PositionChanged -= LargeLineCaret_Changed;
        largeLineGenerator.NavigateRequested -= NavigateLargeLine;
        Editor.TextArea.TextView.ElementGenerators.Remove(largeLineGenerator);
    }
}
