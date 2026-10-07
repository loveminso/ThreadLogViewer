using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private void MainMenu_Opened(object sender, RoutedEventArgs e)
    { ReleaseUnusedLineActionCaptureForKeyboard(Mouse.RightButton == MouseButtonState.Released); UpdateMenus(); }

    private void UpdateMenus()
    {
        if (MainMenu is null) return;
        bool hasLog = projection is not null && !IsBlankSession;
        EncodingMenu.IsEnabled = !busy && requestedPath is not null && ActiveScope is null;
        RefreshSessionMenu.IsEnabled = CanRefreshCurrentSession;
        RestoreSessionMenu.IsEnabled = CanRestoreClosedSession;
        BackNavigationMenu.IsEnabled = !busy && activeSession?.History.Back.Count > 0;
        ForwardNavigationMenu.IsEnabled = !busy && activeSession?.History.Forward.Count > 0;
        ThreadBackgroundMenu.IsChecked = threadRenderer.Enabled;
        LargeLineMenu.IsChecked = largeLineGenerator.Enabled;
        SaveAnalysisMenu.IsEnabled = SavePresetMenu.IsEnabled = !busy && hasLog;
        LoadAnalysisMenu.IsEnabled = !busy;
        LoadPresetMenu.IsEnabled = !busy && hasLog;
        WrapMenu.IsChecked = WrapBox.IsChecked == true;
        foreach (var item in FontMenu.Items.OfType<MenuItem>())
            if (item.Tag is string tag && tag.StartsWith("font:", StringComparison.Ordinal))
                item.IsChecked = FontSizeBox.SelectedItem is ComboBoxItem font && font.Content.ToString() == tag[5..];
        CheckAppearanceGroup(DensityMenu, "density:", DensityBox.SelectedIndex);
        CheckAppearanceGroup(ThemeMenu, "theme:", ThemeBox.SelectedIndex);
        CloseSessionMenu.IsEnabled = SessionTabs.Items.Count > 0;
        NextSessionMenu.IsEnabled = PreviousSessionMenu.IsEnabled = SessionTabs.Items.Count > 1;
        AnalysisMenu.IsEnabled = FindMenu.IsEnabled = GoToMenu.IsEnabled = !busy && hasLog;
        FindNextMenu.IsEnabled = FindPreviousMenu.IsEnabled = !busy && searchHits.Length > 0;
        ExportMenu.Header = ExportLabel.Text + "…";
        int? line = LineActionSourceLine();
        bool hasTime = data is not null && line is { } sourceLine && LogTimeAnalysis.ResolveAnchor(data, sourceLine) is not null;
        foreach (var item in AnalysisMenu.Items.OfType<MenuItem>())
            item.IsEnabled = (item.Tag as string) switch
            {
                "line" => line is not null && !busy,
                "highlight" => hasLog && !busy,
                "highlight-manager" => hasLog && !busy,
                "split-end" => line is not null && separationStartLine is not null && !busy,
                "split-clear" => separationStartLine is not null,
                "time" => hasTime && !busy,
                "time-compare" => hasTime && timeA is not null && !busy,
                "time-clear" => timeA is not null || timeB is not null,
                _ => !busy && hasLog
            };
    }
    private void TimeResultMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsEnabled = (item.Tag as string) switch
            {
                "anchor-a" => timeA is not null && !busy,
                "anchor-b" => timeB is not null && !busy,
                "time-pair" => timeA is not null && timeB is not null,
                _ => timeA is not null || timeB is not null
            };
    }

    private static void CheckAppearanceGroup(MenuItem group, string prefix, int selected)
    {
        foreach (var item in group.Items.OfType<MenuItem>())
            if (item.Tag is string tag) item.IsChecked = tag == prefix + selected.ToString(CultureInfo.InvariantCulture);
    }

    private void MenuWrap_Click(object sender, RoutedEventArgs e)
    {
        WrapBox.IsChecked = WrapMenu.IsChecked;
        UpdateMenus();
    }
    private void ThreadBackgroundMenu_Click(object sender, RoutedEventArgs e)
    {
        threadRenderer.Enabled = ThreadBackgroundMenu.IsChecked;
        Editor.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
        ScheduleSettingsSave();
        OperationStatus.Text = threadRenderer.Enabled ? "스레드별 배경색 켜짐" : "스레드별 배경색 꺼짐";
    }

    private void MenuAppearance_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag }) return;
        if (tag.StartsWith("font:", StringComparison.Ordinal))
        {
            foreach (ComboBoxItem item in FontSizeBox.Items)
                if (item.Content.ToString() == tag[5..]) { FontSizeBox.SelectedItem = item; break; }
        }
        else if (tag.StartsWith("density:", StringComparison.Ordinal)) DensityBox.SelectedIndex = int.Parse(tag[8..], CultureInfo.InvariantCulture);
        else if (tag.StartsWith("theme:", StringComparison.Ordinal)) ThemeBox.SelectedIndex = int.Parse(tag[6..], CultureInfo.InvariantCulture);
        UpdateMenus();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ChangeFontSize(1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ChangeFontSize(-1);
    private void ZoomReset_Click(object sender, RoutedEventArgs e) { FontSizeBox.SelectedIndex = 3; UpdateMenus(); }
    private void ChangeFontSize(int step)
    {
        FontSizeBox.SelectedIndex = Math.Clamp(FontSizeBox.SelectedIndex + step, 0, FontSizeBox.Items.Count - 1);
        UpdateMenus();
    }

    private bool TryHandleMenuShortcut(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers != ModifierKeys.Control && modifiers != (ModifierKeys.Control | ModifierKeys.Shift)) return false;
        if (e.Key is Key.Add or Key.OemPlus) ChangeFontSize(1);
        else if (modifiers == ModifierKeys.Control && (e.Key is Key.Subtract or Key.OemMinus)) ChangeFontSize(-1);
        else if (modifiers == ModifierKeys.Control && (e.Key is Key.D0 or Key.NumPad0)) ZoomReset_Click(this, new());
        else return false;
        e.Handled = true;
        return true;
    }

    private async void ReloadAuto_Click(object sender, RoutedEventArgs e) => await ReloadCurrentAsync(EncodingMode.Auto);
    private void MenuAnalysis_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && int.TryParse(tag, out int tool)) ShowAnalysisTool(tool);
    }
    private void CloseAnalysis_Click(object sender, RoutedEventArgs e) => AnalysisPanel.IsExpanded = false;
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
