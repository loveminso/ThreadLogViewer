using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ThreadLogViewer.App;

// Both Explorer drag/drop and Explorer copy/paste use FileDrop. Read the payload on the
// STA UI thread, then pass only a file path or immutable text to the background loader.
public sealed record LogTransferContent(string? FilePath, string? Text);

public static class LogTransfer
{
    public static bool IsSingleLogFile(string[]? files) => files is { Length: 1 }
        && !string.IsNullOrWhiteSpace(files[0])
        && (string.Equals(Path.GetExtension(files[0]), ".log", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(files[0]), ".txt", StringComparison.OrdinalIgnoreCase));

    public static string ReadDroppedFile(IDataObject data)
    {
        var files = data.GetData(DataFormats.FileDrop) as string[];
        if (!IsSingleLogFile(files)) throw new InvalidDataException(".log 또는 .txt 파일을 한 번에 하나씩 열어 주세요.");
        return files![0];
    }

    public static LogTransferContent ReadClipboard(IDataObject? data)
    {
        if (data?.GetDataPresent(DataFormats.FileDrop) == true)
            return new(ReadDroppedFile(data), null);
        if (data?.GetDataPresent(DataFormats.UnicodeText) == true
            && data.GetData(DataFormats.UnicodeText) is string { Length: > 0 } unicode)
            return new(null, unicode);
        if (data?.GetDataPresent(DataFormats.Text) == true
            && data.GetData(DataFormats.Text) is string { Length: > 0 } text)
            return new(null, text);
        throw new InvalidDataException("클립보드에 로그 텍스트 또는 .log/.txt 파일 하나를 복사한 뒤 붙여넣으세요.");
    }

    public static bool OpensLogOnPaste(Key key, ModifierKeys modifiers, IInputElement? focus)
    {
        // Search and other editable fields retain normal paste. Ctrl+Shift+V explicitly
        // opens the clipboard as a log from anywhere in the app.
        if (key == Key.V && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) return true;
        bool normalPaste = (key == Key.V && modifiers == ModifierKeys.Control)
            || (key == Key.Insert && modifiers == ModifierKeys.Shift);
        return normalPaste && focus is not TextBoxBase and not PasswordBox;
    }
}
