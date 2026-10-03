using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private int blankSessionNumber;
    private bool IsBlankSession => activeSession?.IsBlank == true;

    private void NewSession_Click(object sender, RoutedEventArgs e) => CreateBlankSession();

    private void CreateBlankSession()
    {
        if (!viewReady) return;
        fileBatchVersion++;
        CancelSessionWork();
        var source = new LogData(null, "", "입력 대기", [], [], []);
        var view = LogProjection.Create(source, []);
        var document = new TextDocument { UndoStack = { SizeLimit = 0 } };
        CommitLoadedSession(source, view, document, null, title: $"새 탭 {++blankSessionNumber}", isBlank: true);
        OperationStatus.Text = "새 탭을 만들었습니다. Ctrl+V로 로그를 붙여넣거나 Ctrl+O로 파일을 여세요.";
        Editor.Focus();
    }

    private void UpdateBlankSessionHint()
    {
        if (!IsBlankSession) return;
        EmptyPanel.Visibility = Visibility.Visible;
        EmptyHint.Text = "새 탭에 로그를 추가하세요.";
        EmptyDetail.Text = "Ctrl+V 로그 붙여넣기 · Ctrl+O 파일 열기";
    }
}
