using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace ThreadLogViewer.UiTests;

internal sealed record UiReviewFinding(string Category, string State, string Theme, string Size, int Scale, string Detail);

internal sealed record UiReviewBucket(string State, string Theme, int Scale, int Stored, int Dropped)
{
    public bool Capped => Dropped > 0;
}

internal sealed class UiReviewLint
{
    internal const int FindingCap = 400;
    internal const string TruncatedText = "truncated-text";
    internal const string OutsideBounds = "outside-bounds";
    internal const string TabOrder = "tab-order";
    internal const string MissingName = "missing-name";

    private static readonly ControlType[] NamedControls =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.ComboBox, ControlType.Edit,
        ControlType.Hyperlink, ControlType.ListItem, ControlType.MenuItem, ControlType.RadioButton,
        ControlType.Slider, ControlType.Spinner, ControlType.SplitButton, ControlType.TabItem,
        ControlType.Document, ControlType.Thumb
    ];

    private static readonly ControlType[] TabStops =
    [
        ControlType.Button, ControlType.CheckBox, ControlType.ComboBox, ControlType.Edit,
        ControlType.Hyperlink, ControlType.RadioButton, ControlType.Slider, ControlType.Spinner,
        ControlType.SplitButton, ControlType.TabItem, ControlType.Document, ControlType.Thumb,
        ControlType.List
    ];

    private readonly List<UiReviewFinding> findings = [];
    private readonly Dictionary<BucketKey, BucketCount> buckets = [];
    private readonly Font measureFont;

    internal UiReviewLint(double contentScale)
    {
        float size = (float)(13.0 * contentScale);
        try { measureFont = new Font("Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point); }
        catch (ArgumentException) { measureFont = new Font(FontFamily.GenericSansSerif, size, FontStyle.Regular, GraphicsUnit.Point); }
    }

    internal IReadOnlyList<UiReviewFinding> Findings => findings;

    internal IReadOnlyList<UiReviewBucket> Buckets => buckets.Select(pair =>
        new UiReviewBucket(pair.Key.State, pair.Key.Theme, pair.Key.Scale, pair.Value.Stored, pair.Value.Dropped)).ToArray();

    internal List<string> LastTabOrder { get; } = [];

    public void DisposeFont() => measureFont.Dispose();

    internal void Inspect(UIA3Automation automation, IEnumerable<AutomationElement> roots, AutomationElement tabRoot, string state, string theme, string size, int scale)
    {
        LastTabOrder.Clear();
        foreach (var root in roots)
        {
            Rectangle window = SafeBounds(root);
            var seen = new HashSet<string>();
            Walk(root, root, window, state, theme, size, scale, 0, seen);
        }
        try
        {
            InspectTabOrder(automation, tabRoot, state, theme, size, scale);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Add(TabOrder, state, theme, size, scale, "Tab walk failed: " + ex.GetType().Name);
        }
    }

    private void Walk(AutomationElement element, AutomationElement root, Rectangle window, string state, string theme, string size, int scale, int depth, HashSet<string> seen)
    {
        if (depth > 24 || !seen.Add(Identity(element)))
            return;
        if (!ReferenceEquals(element, root))
        {
            try
            {
                if (!element.Properties.IsOffscreen.ValueOrDefault)
                {
                    InspectName(element, state, theme, size, scale);
                    InspectTruncation(element, state, theme, size, scale);
                    InspectBounds(element, root, window, state, theme, size, scale);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        }
        AutomationElement[] children;
        try { children = element.FindAllChildren(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
        foreach (var child in children)
            Walk(child, root, window, state, theme, size, scale, depth + 1, seen);
    }

    private void InspectName(AutomationElement element, string state, string theme, string size, int scale)
    {
        if (!Is(TypeOf(element), NamedControls))
            return;
        if (!element.Properties.IsEnabled.ValueOrDefault)
            return;
        if (!string.IsNullOrWhiteSpace(element.Properties.Name.ValueOrDefault))
            return;
        Add(MissingName, state, theme, size, scale, Describe(element) + " has no accessible name");
    }

    private void InspectTruncation(AutomationElement element, string state, string theme, string size, int scale)
    {
        if (TypeOf(element) != ControlType.Text && TypeOf(element) != ControlType.Header)
            return;
        string name = Clean(element.Properties.Name.ValueOrDefault);
        if (name.Length == 0)
            return;
        // Menu headers expose "파일(_F)" and an ellipsis that is not drawn text. Other ellipsis stays.
        if (ParentIsMenuItem(element))
            return;
        Rectangle bounds = SafeBounds(element);
        if (bounds.Width <= 1 || bounds.Height <= 1)
            return;
        bool ellipsis = name.Contains('…') || name.EndsWith("...", StringComparison.Ordinal);
        string help = Clean(element.Properties.HelpText.ValueOrDefault);
        if (ellipsis && help.Length > name.Length)
        {
            Add(TruncatedText, state, theme, size, scale, "Ellipsis trimming on " + Describe(element));
            return;
        }
        double line = 32.0 * (scale == 150 ? 1.5 : 1.0);
        if (bounds.Height > line)
            return;
        int desired = DesiredWidth(name);
        int excess = desired - bounds.Width;
        if (excess <= 8)
            return;
        bool strong = desired > bounds.Width * 1.25 + 8;
        bool stretched = false;
        try
        {
            var parent = element.Parent;
            if (parent is not null)
            {
                Rectangle parentBounds = SafeBounds(parent);
                stretched = bounds.Right >= parentBounds.Right - 8;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }
        if (!strong && !stretched && !ellipsis)
            return;
        Add(TruncatedText, state, theme, size, scale,
            Describe(element) + " estimated width " + desired + "px exceeds bounds " + bounds.Width + "px");
    }

    private void InspectBounds(AutomationElement element, AutomationElement root, Rectangle window, string state, string theme, string size, int scale)
    {
        Rectangle bounds = SafeBounds(element);
        if (bounds.Width <= 1 || bounds.Height <= 1 || window.Width <= 1)
            return;
        if (IsSystemMenuBar(element) || IsMenuPopup(element))
            return;
        if (Scrolls(element) || AncestorScrolls(element, root))
            return;
        if (!Contains(window, bounds, 6))
        {
            Add(OutsideBounds, state, theme, size, scale, Describe(element) + " is outside the window " + Format(bounds));
            return;
        }
        try
        {
            var parent = element.Parent;
            if (parent is null || ReferenceEquals(parent, root) || Scrolls(parent))
                return;
            Rectangle parentBounds = SafeBounds(parent);
            if (parentBounds.Width <= 1 || parentBounds.Height <= 1)
                return;
            if (!Contains(parentBounds, bounds, 6))
                Add(OutsideBounds, state, theme, size, scale, Describe(element) + " is outside its parent " + Format(bounds));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
        }
    }

    private void InspectTabOrder(UIA3Automation automation, AutomationElement root, string state, string theme, string size, int scale)
    {
        var stops = new List<(string Id, string Label)>();
        CollectStops(root, stops, 0, new HashSet<string>());
        try { root.Focus(); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException) { }
        Thread.Sleep(40);
        var hits = new List<(string Id, string Label, Rectangle Bounds)>();
        var seen = new HashSet<string>();
        for (int step = 0; step < 40; step++)
        {
            Keyboard.Type(VirtualKeyShort.TAB);
            Thread.Sleep(25);
            AutomationElement? focused;
            try { focused = automation.FocusedElement(); }
            catch (Exception ex) when (ex is COMException or InvalidOperationException) { break; }
            if (focused is null)
                break;
            string id = Identity(focused);
            if (hits.Count > 0 && id == hits[0].Id)
                break;
            if (!seen.Add(id))
                break;
            var hit = (id, Label(focused), SafeBounds(focused));
            hits.Add(hit);
            LastTabOrder.Add(hit.Item2);
        }
        int unreachable = 0;
        foreach (var stop in stops)
        {
            if (seen.Contains(stop.Id))
                continue;
            unreachable++;
            if (unreachable <= 25)
                Add(TabOrder, state, theme, size, scale, "Unreachable by Tab: " + stop.Label);
        }
        if (unreachable > 25)
            Add(TabOrder, state, theme, size, scale, (unreachable - 25) + " more Tab stops were not reached");
        int jumps = 0;
        for (int i = 1; i < hits.Count; i++)
        {
            var previous = hits[i - 1].Bounds;
            var next = hits[i].Bounds;
            if (previous.Width <= 1 || next.Width <= 1)
                continue;
            bool upward = next.Top + 80 < previous.Top;
            bool sameColumn = Math.Abs(next.Left - previous.Left) < 140;
            if (!upward || !sameColumn)
                continue;
            jumps++;
            if (jumps <= 12)
                Add(TabOrder, state, theme, size, scale, "Tab order jumps from " + hits[i - 1].Label + " to " + hits[i].Label);
        }
        if (hits.Count >= 40 && stops.Exists(stop => !seen.Contains(stop.Id)))
            Add(TabOrder, state, theme, size, scale, "Tab sequence did not cycle within 40 steps");
    }

    private static void CollectStops(AutomationElement element, List<(string Id, string Label)> stops, int depth, HashSet<string> seen)
    {
        if (depth > 24 || !seen.Add(Identity(element)))
            return;
        try
        {
            if (element.Properties.IsKeyboardFocusable.ValueOrDefault &&
                element.Properties.IsEnabled.ValueOrDefault &&
                !element.Properties.IsOffscreen.ValueOrDefault &&
                Is(TypeOf(element), TabStops))
                stops.Add((Identity(element), Label(element)));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
        AutomationElement[] children;
        try { children = element.FindAllChildren(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return; }
        foreach (var child in children)
            CollectStops(child, stops, depth + 1, seen);
    }

    private int DesiredWidth(string text)
    {
        string sample = text.Length > 180 ? text[..180] : text;
        var measured = System.Windows.Forms.TextRenderer.MeasureText(
            sample,
            measureFont,
            new Size(int.MaxValue, int.MaxValue),
            System.Windows.Forms.TextFormatFlags.NoPadding | System.Windows.Forms.TextFormatFlags.SingleLine | System.Windows.Forms.TextFormatFlags.NoPrefix);
        if (text.Length > 180)
            return measured.Width + 80;
        return measured.Width;
    }

    private void Add(string category, string state, string theme, string size, int scale, string detail)
    {
        var key = new BucketKey(state, theme, scale);
        if (!buckets.TryGetValue(key, out var count))
        {
            count = new BucketCount();
            buckets[key] = count;
        }
        if (count.Stored >= FindingCap)
        {
            count.Dropped++;
            return;
        }
        count.Stored++;
        findings.Add(new UiReviewFinding(category, state, theme, size, scale, Clean(detail)));
    }

    private readonly record struct BucketKey(string State, string Theme, int Scale);

    private sealed class BucketCount
    {
        public int Stored;
        public int Dropped;
    }

    private static bool IsSystemMenuBar(AutomationElement element)
    {
        string id = Clean(element.Properties.AutomationId.ValueOrDefault);
        if (id.Equals("SystemMenuBar", StringComparison.OrdinalIgnoreCase))
            return true;
        string name = Clean(element.Properties.Name.ValueOrDefault);
        return TypeOf(element) == ControlType.MenuBar && name.Equals("System", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMenuPopup(AutomationElement element)
    {
        var type = TypeOf(element);
        return type == ControlType.Menu || type == ControlType.MenuItem;
    }

    private static bool ParentIsMenuItem(AutomationElement element)
    {
        try
        {
            var parent = element.Parent;
            return parent is not null && TypeOf(parent) == ControlType.MenuItem;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Is(ControlType actual, params ControlType[] types)
    {
        foreach (var type in types)
            if (actual == type)
                return true;
        return false;
    }

    private static bool AncestorScrolls(AutomationElement element, AutomationElement root)
    {
        AutomationElement? current = element;
        for (int depth = 0; depth < 8 && current is not null && !ReferenceEquals(current, root); depth++)
        {
            try
            {
                if (Scrolls(current))
                    return true;
                current = current.Parent;
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                return false;
            }
        }
        return false;
    }

    private static bool Scrolls(AutomationElement element)
    {
        try
        {
            if (!element.Patterns.Scroll.TryGetPattern(out var scroll))
                return false;
            return scroll.HorizontallyScrollable.ValueOrDefault || scroll.VerticallyScrollable.ValueOrDefault;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Contains(Rectangle outer, Rectangle inner, int slack) =>
        inner.Left >= outer.Left - slack &&
        inner.Top >= outer.Top - slack &&
        inner.Right <= outer.Right + slack &&
        inner.Bottom <= outer.Bottom + slack;

    private static Rectangle SafeBounds(AutomationElement element)
    {
        try { return element.BoundingRectangle; }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return Rectangle.Empty;
        }
    }

    private static ControlType TypeOf(AutomationElement element)
    {
        try { return element.Properties.ControlType.ValueOrDefault; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return ControlType.Unknown; }
    }

    private static string Identity(AutomationElement element)
    {
        try
        {
            int[] runtime = element.Properties.RuntimeId.ValueOrDefault ?? [];
            string id = element.Properties.AutomationId.ValueOrDefault ?? "";
            return TypeOf(element) + "|" + id + "|" + string.Join('.', runtime);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Guid.NewGuid().ToString("N");
        }
    }

    private static string Label(AutomationElement element)
    {
        string name = Clean(element.Properties.Name.ValueOrDefault);
        string id = Clean(element.Properties.AutomationId.ValueOrDefault);
        string type = TypeOf(element).ToString();
        if (name.Length > 0 && id.Length > 0)
            return type + " " + id + " \"" + Trim(name) + "\"";
        if (name.Length > 0)
            return type + " \"" + Trim(name) + "\"";
        if (id.Length > 0)
            return type + " " + id;
        return type;
    }

    private static string Describe(AutomationElement element) => Label(element);

    private static string Trim(string value) => value.Length <= 80 ? value : value[..80] + "…";

    private static string Format(Rectangle bounds) => bounds.Left + "," + bounds.Top + " " + bounds.Width + "x" + bounds.Height;

    internal static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        var builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    builder.Append(c);
                    builder.Append(value[++i]);
                }
                else builder.Append('\uFFFD');
            }
            else if (char.IsLowSurrogate(c) || c is '\r' or '\n' or '\t' or '|')
                builder.Append(c == '|' ? '/' : ' ');
            else builder.Append(c);
        }
        string text = builder.ToString().Trim();
        return text.Length <= 240 ? text : text[..240];
    }
}
