using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ThreadLogViewer.App;

// Both Explorer drag/drop and Explorer copy/paste use FileDrop. Read the payload on the
// STA UI thread, then pass only a file path or immutable text to the background loader.
public sealed record LogTransferContent(string? FilePath, string? Text)
{
    public IReadOnlyList<string> FilePaths { get; init; } = FilePath is null
        ? Array.Empty<string>() : Array.AsReadOnly(new[] { FilePath });
}

public static class LogTransfer
{
    public static bool IsSingleLogFile(string[]? files) => files is { Length: 1 } && IsLogFiles(files);

    public static bool IsLogFiles(string[]? files) => files is { Length: > 0 } && files.All(IsLogFile);
    private static bool IsLogFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.IndexOf('\0') >= 0) return false;
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".log", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase);
    }

    public static string ReadDroppedFile(IDataObject data)
    {
        var files = ReadDroppedFiles(data);
        if (files.Count != 1) throw new InvalidDataException(".log 또는 .txt 파일을 한 번에 하나씩 열어 주세요.");
        return files[0];
    }

    public static IReadOnlyList<string> ReadDroppedFiles(IDataObject data)
    {
        var files = ReadPayload(() => data.GetData(DataFormats.FileDrop, false)) as string[];
        if (!IsLogFiles(files)) throw new InvalidDataException(".log 또는 .txt 파일만 선택해 주세요.");
        // OLE providers own their arrays. Snapshot before any async open can yield control.
        return Array.AsReadOnly((string[])files!.Clone());
    }

    public static LogTransferContent ReadClipboard(IDataObject? data) => ReadClipboardCore(data, false);

    public static LogTransferContent ReadClipboardFiles(IDataObject? data) => ReadClipboardCore(data, true);

    private static LogTransferContent ReadClipboardCore(IDataObject? data, bool multipleFiles)
    {
        if (data is not null && ReadPayload(() => data.GetDataPresent(DataFormats.FileDrop, false)))
        {
            var files = ReadDroppedFiles(data);
            if (!multipleFiles && files.Count != 1)
                throw new InvalidDataException(".log 또는 .txt 파일을 한 번에 하나씩 열어 주세요.");
            return new(files[0], null) { FilePaths = files };
        }
        if (data is not null && ReadPayload(() => data.GetDataPresent(DataFormats.UnicodeText, false))
            && ReadPayload(() => data.GetData(DataFormats.UnicodeText, false)) is string { Length: > 0 } unicode)
            return new(null, unicode);
        if (data is not null && ReadPayload(() => data.GetDataPresent(DataFormats.Text, false))
            && ReadPayload(() => data.GetData(DataFormats.Text, false)) is string { Length: > 0 } text)
            return new(null, text);
        throw new InvalidDataException("클립보드에 로그 텍스트 또는 .log/.txt 파일 하나를 복사한 뒤 붙여넣으세요.");
    }

    private static T ReadPayload<T>(Func<T> read)
    {
        try { return read(); }
        // IDataObject may call an external OLE provider. Its managed exception types are
        // not limited to COMException; keep this guard at the provider boundary only.
        catch (Exception ex) when (ex is not OutOfMemoryException)
        { throw new IOException("클립보드 자료를 읽을 수 없습니다. 복사한 프로그램에서 다시 복사한 뒤 시도하세요.", ex); }
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
