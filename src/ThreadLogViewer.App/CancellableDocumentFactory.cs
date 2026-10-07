using ICSharpCode.AvalonEdit.Document;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public static class CancellableDocumentFactory
{
    public static TextDocument Create(string text, CancellationToken token, IProgress<WorkProgress> progress)
    {
        token.ThrowIfCancellationRequested();
        progress.Report(new("화면 문서 준비", 0));
        var document = new TextDocument();
        document.UndoStack.SizeLimit = 0;
        const int block = 262144;
        for (int offset = 0; offset < text.Length; offset += block)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(block, text.Length - offset);
            document.Insert(document.TextLength, text.Substring(offset, count));
            progress.Report(new("화면 문서 준비", 100.0 * (offset + count) / text.Length));
        }
        token.ThrowIfCancellationRequested();
        document.SetOwnerThread(null);
        progress.Report(new("화면 문서 준비", 100));
        return document;
    }
}
