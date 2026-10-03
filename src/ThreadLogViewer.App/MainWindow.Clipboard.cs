using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    // Provider injection keeps regressions synthetic and never touches the user's clipboard.
    private async Task<bool> PasteFromDataObjectAsync(Func<IDataObject?> readClipboard)
    {
        LogTransferContent input;
        try { input = LogTransfer.ReadClipboardFiles(readClipboard()); }
        catch (OutOfMemoryException)
        {
            OperationStatus.Text = "붙여넣기 실패 · 메모리가 부족합니다. 더 작은 범위를 복사해 주세요.";
            return false;
        }
        catch (Exception)
        {
            // No content, source path, or provider diagnostic is written to a log or status.
            OperationStatus.Text = "붙여넣기 실패 · 클립보드의 로그 텍스트 또는 .log/.txt 파일을 다시 복사해 주세요. 현재 로그는 유지됩니다.";
            return false;
        }

        try
        {
            if (input.FilePaths.Count > 0) return await OpenFilesAsync(input.FilePaths);
            return await LoadAsync((token, progress) => Task.FromResult(LogParser.ParsePastedText(input.Text!, token, progress)),
                "붙여넣은 로그 준비…", "붙여넣은 로그를 열 수 없습니다");
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ExternalException
            or InvalidOperationException or NotSupportedException or ArgumentException or SecurityException or OutOfMemoryException)
        {
            OperationStatus.Text = "붙여넣기 실패 · 로그를 열 수 없습니다. 입력을 확인하고 다시 시도하세요.";
            return false;
        }
    }
}
