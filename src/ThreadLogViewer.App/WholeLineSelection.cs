using System.Text;
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

/// <summary>An immutable selection of whole displayed physical lines, including their original delimiters.</summary>
public sealed class WholeLineSelection : Selection
{
    private readonly TextArea area;
    private readonly TextDocument document;
    private readonly LogProjection projection;
    private readonly IReadOnlyList<SelectionSegment> segments;
    private readonly int length;
    public IReadOnlyList<int> DisplayLines { get; }
    public IReadOnlyList<int> SourceLineIndexes { get; }

    public WholeLineSelection(TextArea area, LogProjection projection, IEnumerable<int> displayLines) : base(area)
    {
        this.area = area;
        this.projection = projection;
        document = area.Document;
        int[] lines = displayLines.Distinct().Order().ToArray();
        if (lines.Length == 0) throw new ArgumentException("선택할 원본 줄이 없습니다.", nameof(displayLines));
        var ranges = new List<SelectionSegment>();
        var sourceLines = new List<int>(lines.Length);
        foreach (int line in lines)
        {
            if (projection.AtDisplayLine(line) is not { } row || line > document.LineCount)
                throw new ArgumentOutOfRangeException(nameof(displayLines));
            var physical = document.GetLineByNumber(line);
            int end = physical.Offset + physical.TotalLength;
            if (ranges.Count > 0 && ranges[^1].EndOffset == physical.Offset)
                ranges[^1] = new SelectionSegment(ranges[^1].StartOffset, end);
            else ranges.Add(new SelectionSegment(physical.Offset, end));
            sourceLines.Add(row.OriginalLineNumber - 1);
        }
        segments = ranges.AsReadOnly();
        length = segments.Sum(segment => segment.Length);
        DisplayLines = Array.AsReadOnly(lines);
        SourceLineIndexes = sourceLines.AsReadOnly();
    }

    public bool IsFor(LogProjection view, TextDocument currentDocument) => ReferenceEquals(projection, view) && ReferenceEquals(document, currentDocument);
    public override TextViewPosition StartPosition => new(document.GetLocation(segments[0].StartOffset));
    public override TextViewPosition EndPosition => new(document.GetLocation(segments[^1].EndOffset));
    public override IEnumerable<SelectionSegment> Segments => segments;
    public override ISegment SurroundingSegment => new SelectionSegment(segments[0].StartOffset, segments[^1].EndOffset);
    public override int Length => length;
    public override bool IsEmpty => length == 0;
    public override bool EnableVirtualSpace => false;
    public override bool IsMultiline => DisplayLines.Count > 1 || StartPosition.Line != EndPosition.Line;

    public override string GetText()
    {
        if (!ReferenceEquals(area.Document, document)) return "";
        var text = new StringBuilder(length);
        foreach (var segment in segments) text.Append(projection.Text.AsSpan(segment.StartOffset, segment.Length));
        return text.ToString();
    }
    public override DataObject CreateDataObject(TextArea textArea)
    {
        var data = new DataObject();
        string text = GetText();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData(DataFormats.Text, text);
        return data;
    }
    public override bool Contains(int offset) => segments.Any(segment => offset >= segment.StartOffset && offset < segment.EndOffset)
        || offset == segments[^1].EndOffset;
    // Selection gestures never acquire permission to modify a source or an editor document.
    public override void ReplaceSelectionWithText(string newText) { }
    public override Selection UpdateOnDocumentChange(DocumentChangeEventArgs e) => Create(area, Math.Clamp(area.Caret.Offset, 0, area.Document.TextLength), Math.Clamp(area.Caret.Offset, 0, area.Document.TextLength));
    public override Selection SetEndpoint(TextViewPosition endPosition) => Create(area, document.GetOffset(StartPosition.Location), document.GetOffset(endPosition.Location));
    public override Selection StartSelectionOrSetEndpoint(TextViewPosition startPosition, TextViewPosition endPosition) => Create(area, document.GetOffset(startPosition.Location), document.GetOffset(endPosition.Location));
    public override bool Equals(object? obj) => obj is WholeLineSelection other && ReferenceEquals(area, other.area)
        && ReferenceEquals(document, other.document) && ReferenceEquals(projection, other.projection) && DisplayLines.SequenceEqual(other.DisplayLines);
    public override int GetHashCode() => HashCode.Combine(area, document, projection, segments[0].StartOffset, segments[^1].EndOffset, length);
}
