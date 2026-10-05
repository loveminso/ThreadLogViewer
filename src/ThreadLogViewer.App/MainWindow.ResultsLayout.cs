using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private const double MinimumResultsHeight = 80, MaximumResultsHeight = 1200;
    private const double MinimumLogHeight = 120, ResultsSplitterHeight = 6, ResultsHeaderHeight = 43;
    private double preferredResultsHeight = 180;
    private bool resultsCollapsed, resultsLayoutReady, updatingResultsLayout, resultsDragActive, resultsDragChanged;
    private double resultsDragHeight, resultsDragDisplayHeight;
    private DependencyPropertyDescriptor? resultsVisibilityDescriptor;

    private void InitializeResultsLayout(double height, bool collapsed)
    {
        preferredResultsHeight = double.IsFinite(height) && height is >= MinimumResultsHeight and <= MaximumResultsHeight ? height : 180;
        resultsCollapsed = collapsed;
        resultsVisibilityDescriptor = DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(Border));
        resultsVisibilityDescriptor?.AddValueChanged(ResultsPanel, ResultsVisibility_Changed);
        LogContentGrid.LayoutUpdated += ResultsGrid_LayoutUpdated;
        LogContentGrid.SizeChanged += ResultsGrid_SizeChanged;
        resultsLayoutReady = true;
        UpdateResultsLayout();
    }

    private void ResultsVisibility_Changed(object? sender, EventArgs e) => UpdateResultsLayout();
    private void ResultsGrid_LayoutUpdated(object? sender, EventArgs e) => UpdateResultsLayout();
    private void ResultsGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateResultsLayout();

    private double ResultsAvailableHeight()
    {
        if (LogContentGrid.ActualHeight <= 0) return double.PositiveInfinity;
        // Fixed result rows can inflate both grids beyond the DockPanel's allocation.
        // ActualHeight then describes overflow, so also honor the parent's layout slot.
        double viewport = LimitResultsViewportToSlot(LogContentGrid.ActualHeight, LogContentGrid);
        if (LogContentGrid.Parent is FrameworkElement parent)
            viewport = LimitResultsViewportToSlot(viewport, parent);
        double top = LogContentGrid.RowDefinitions.Take(4).Sum(row => row.ActualHeight);
        return Math.Max(0, viewport - top);
    }

    private static double LimitResultsViewportToSlot(double height, FrameworkElement element)
    {
        Rect slot = LayoutInformation.GetLayoutSlot(element);
        return !slot.IsEmpty && double.IsFinite(slot.Height) ? Math.Min(height, slot.Height) : height;
    }

    private double ResultsHeaderDesiredHeight() => Math.Max(ResultsHeaderHeight,
        ResultsHeader.DesiredSize.Height + ResultsPanel.Padding.Top + ResultsPanel.Padding.Bottom +
        ResultsPanel.BorderThickness.Top + ResultsPanel.BorderThickness.Bottom);

    private void UpdateResultsLayout()
    {
        if (!resultsLayoutReady || updatingResultsLayout) return;
        updatingResultsLayout = true;
        try
        {
            bool shown = ResultsPanel.Visibility == Visibility.Visible;
            double available = ResultsAvailableHeight();
            double header = ResultsHeaderDesiredHeight();
            double splitter = shown && !resultsCollapsed && available > header ? ResultsSplitterHeight : 0;
            double editorMinimum = shown ? Math.Min(MinimumLogHeight, Math.Max(0, available - header - splitter)) : Math.Min(MinimumLogHeight, available);
            double maximum = Math.Max(0, available - editorMinimum - splitter);

            SetResultsRowHeight(EditorRow, new GridLength(1, GridUnitType.Star));
            if (EditorRow.MinHeight != editorMinimum) EditorRow.MinHeight = editorMinimum;
            var listVisibility = resultsCollapsed ? Visibility.Collapsed : Visibility.Visible;
            if (ResultsList.Visibility != listVisibility) ResultsList.Visibility = listVisibility;
            SetResultsRowHeight(ResultsBodyRow, resultsCollapsed ? GridLength.Auto : new GridLength(1, GridUnitType.Star));
            var splitterVisibility = splitter > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ResultsSplitter.Visibility != splitterVisibility) ResultsSplitter.Visibility = splitterVisibility;
            SetResultsRowHeight(ResultsSplitterRow, new GridLength(splitter));

            double minimum = shown && !resultsCollapsed ? Math.Min(MinimumResultsHeight, maximum) : 0;
            double rowMaximum = shown ? resultsCollapsed ? available : maximum : 0;
            if (ResultsRow.MinHeight != minimum) ResultsRow.MinHeight = minimum;
            if (ResultsRow.MaxHeight != rowMaximum) ResultsRow.MaxHeight = rowMaximum;
            SetResultsRowHeight(ResultsRow, !shown ? new GridLength(0) : resultsCollapsed ? GridLength.Auto :
                new GridLength(Math.Clamp(resultsDragActive ? resultsDragHeight : preferredResultsHeight, minimum, maximum)));

            string toggle = resultsCollapsed ? "펼치기" : "접기";
            if (!Equals(ResultsToggleButton.Content, toggle))
            {
                ResultsToggleButton.Content = toggle;
                AutomationProperties.SetName(ResultsToggleButton, "검색 결과 목록 " + toggle);
                ResultsToggleButton.ToolTip = resultsCollapsed ? "검색 결과 목록 펼치기" : "검색과 강조를 유지하고 결과 목록만 접기";
            }
            if (resultsDragActive && ResultsRow.Height.IsAbsolute) resultsDragDisplayHeight = ResultsRow.Height.Value;
        }
        finally { updatingResultsLayout = false; }
    }

    private static void SetResultsRowHeight(RowDefinition row, GridLength height)
    {
        if (row.Height != height) row.Height = height;
    }

    private void ResultsToggle_Click(object sender, RoutedEventArgs e)
    {
        resultsCollapsed = !resultsCollapsed;
        UpdateResultsLayout();
        ScheduleSettingsSave();
    }

    private bool SetResultsHeightFromUser(double requested, double before)
    {
        if (!double.IsFinite(requested) || resultsCollapsed || ResultsPanel.Visibility != Visibility.Visible) return false;
        double maximum = Math.Min(MaximumResultsHeight, ResultsRow.MaxHeight);
        double height = Math.Clamp(requested, Math.Min(MinimumResultsHeight, maximum), maximum);
        if (Math.Abs(height - before) < 0.1) { UpdateResultsLayout(); return false; }
        double preference = Math.Clamp(height, MinimumResultsHeight, MaximumResultsHeight);
        bool changed = Math.Abs(preference - (resultsDragActive ? resultsDragHeight : preferredResultsHeight)) >= 0.1;
        if (resultsDragActive) { resultsDragHeight = preference; resultsDragChanged |= changed; }
        else preferredResultsHeight = preference;
        UpdateResultsLayout();
        return changed;
    }

    private void ResultsSplitter_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (resultsCollapsed || ResultsPanel.Visibility != Visibility.Visible) return;
        resultsDragActive = true;
        resultsDragChanged = false;
        resultsDragHeight = resultsDragDisplayHeight = ResultsRow.Height.Value;
    }

    private void ResultsSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!resultsDragActive) return;
        // GridSplitter's native row resize runs first; retain only the user's chosen height.
        SetResultsHeightFromUser(resultsDragDisplayHeight - e.VerticalChange, resultsDragDisplayHeight);
        e.Handled = true;
    }

    private void ResultsSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (!resultsDragActive) return;
        resultsDragActive = false;
        if (!e.Canceled && resultsDragChanged && Math.Abs(resultsDragHeight - preferredResultsHeight) >= 0.1)
        {
            preferredResultsHeight = resultsDragHeight;
            ScheduleSettingsSave();
        }
        resultsDragChanged = false;
        UpdateResultsLayout();
    }

    private void ResultsSplitter_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || resultsCollapsed || ResultsPanel.Visibility != Visibility.Visible) return;
        double before = ResultsRow.Height.Value;
        double step = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift) ? 50 : 10;
        if (SetResultsHeightFromUser(before + (e.Key == Key.Up ? step : -step), before) && !resultsDragActive) ScheduleSettingsSave();
        e.Handled = true;
    }

    private bool TryCancelResultsResize(KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !resultsDragActive) return false;
        e.Handled = true;
        ResultsSplitter.CancelDrag();
        // Routed test events and an interrupted capture may have no active Thumb gesture.
        if (resultsDragActive) ResultsSplitter_DragCompleted(ResultsSplitter, new DragCompletedEventArgs(0, 0, true));
        return true;
    }

    private void DisposeResultsLayout()
    {
        resultsLayoutReady = false;
        resultsDragActive = false;
        resultsVisibilityDescriptor?.RemoveValueChanged(ResultsPanel, ResultsVisibility_Changed);
        resultsVisibilityDescriptor = null;
        LogContentGrid.LayoutUpdated -= ResultsGrid_LayoutUpdated;
        LogContentGrid.SizeChanged -= ResultsGrid_SizeChanged;
    }
}
