using System.Globalization;
using System.Text;
using System.Windows;
using ICSharpCode.AvalonEdit.Editing;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private const int MaximumQuickHighlightPhraseLength = HighlightPrompt.MaximumPhraseLength;
    private static readonly string[] QuickHighlightColorNames = ["주황", "보라", "청록", "파랑", "분홍", "초록"];

    private void ToggleHighlight_Click(object sender, RoutedEventArgs e) => ToggleHighlightAtSelection();

    private bool ToggleHighlightAtSelection()
    {
        if (!viewReady || data is null || projection is null || projection.Source != data || busy)
        { OperationStatus.Text = "로그를 연 뒤 문구를 선택하거나 단어에 커서를 두세요."; return false; }
        bool captured = lineActionMenuOpen || lineActionPointerPending || lineActionInvalidated;
        if (captured && LineActionSourceLine() is null)
        { OperationStatus.Text = "줄 대상이 변경되었습니다. 로그의 줄을 다시 우클릭하세요."; return false; }

        Selection selection = captured && lineActionSelection is not null ? lineActionSelection : Editor.TextArea.Selection;
        var segments = selection.Segments.Where(segment => segment.Length > 0).ToArray();
        if (segments.Length > 1)
        { OperationStatus.Text = "문구 강조는 연속된 문구 하나를 선택하세요."; return false; }
        string phrase;
        if (segments.Length == 1)
        {
            var segment = segments[0];
            if (segment.Length > MaximumQuickHighlightPhraseLength)
            { OperationStatus.Text = "강조할 문구는 4,096자 이내로 선택하세요."; return false; }
            if (segment.StartOffset < 0 || segment.EndOffset > Editor.Document.TextLength)
            { OperationStatus.Text = "선택한 문구가 변경되었습니다. 다시 선택하세요."; return false; }
            phrase = Editor.Document.GetText(segment.StartOffset, segment.Length);
        }
        else phrase = captured ? lineActionHighlightSeed ?? "" : WordHighlightSeed(Editor.TextArea.Caret.Offset);
        if (string.IsNullOrWhiteSpace(phrase))
        { OperationStatus.Text = "강조할 문구를 선택하거나 단어에 커서를 두고 Shift+F8을 누르세요."; return false; }

        var matching = keywordRules.Where(rule => string.Equals(rule.Keyword, phrase, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matching.Length > 0)
        {
            foreach (var rule in matching)
            { rule.PropertyChanged -= Keyword_Changed; keywordRules.Remove(rule); }
            _ = RefreshKeywordsAsync();
            OperationStatus.Text = $"문구 강조를 해제했습니다. · {keywordRules.Count}/8개";
            return true;
        }
        if (keywordRules.Count >= HighlightPrompt.MaximumRules)
        { OperationStatus.Text = "강조 문구는 최대 8개입니다. 기존 문구에서 Shift+F8로 해제하거나 ‘강조 문구 관리’에서 문구를 삭제·수정하세요. 사용만 끄면 등록 수는 유지됩니다."; return false; }
        int colorIndex = Enumerable.Range(0, QuickHighlightColorNames.Length)
            .FirstOrDefault(color => keywordRules.All(rule => rule.ColorIndex != color), -1);
        if (colorIndex < 0) colorIndex = keywordRules.Count % QuickHighlightColorNames.Length;
        var added = new KeywordRuleItem(phrase, colorIndex);
        added.PropertyChanged += Keyword_Changed;
        keywordRules.Add(added);
        _ = RefreshKeywordsAsync();
        OperationStatus.Text = $"문구 강조 추가 · {QuickHighlightColorNames[colorIndex]} · {keywordRules.Count}/8개 · 같은 문구에서 Shift+F8로 해제";
        return true;
    }

    internal static string ExtractHighlightWord(string text, int caretOffset)
    {
        if (caretOffset < 0 || caretOffset > text.Length || text.Length == 0) return "";
        int anchor = RuneStart(text, caretOffset);
        if (!WordRuneAt(text, anchor, out _))
        {
            if (caretOffset == 0) return "";
            anchor = RuneStart(text, caretOffset - 1);
            if (!WordRuneAt(text, anchor, out int previousLength) || anchor + previousLength != caretOffset) return "";
        }
        int start = anchor, end = anchor;
        while (start > 0)
        {
            int previous = RuneStart(text, start - 1);
            if (!WordRuneAt(text, previous, out _)) break;
            start = previous;
            if (anchor - start > MaximumQuickHighlightPhraseLength) return "";
        }
        while (WordRuneAt(text, end, out int length))
        {
            end += length;
            if (end - start > MaximumQuickHighlightPhraseLength) return "";
        }
        return text[start..end];
    }

    private static int RuneStart(string text, int offset) => offset > 0 && offset < text.Length
        && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1]) ? offset - 1 : offset;

    private static bool WordRuneAt(string text, int offset, out int length)
    {
        length = 0;
        if (offset < 0 || offset >= text.Length || Rune.DecodeFromUtf16(text.AsSpan(offset), out var rune, out length) != System.Buffers.OperationStatus.Done) return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.ConnectorPunctuation;
    }
}
