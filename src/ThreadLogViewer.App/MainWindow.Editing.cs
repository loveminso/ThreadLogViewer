using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private IInputElement? lastEditingTarget;

    private void InitializeEditing()
    {
        lastEditingTarget = Editor.TextArea;
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(Editing_FocusChanged), true);
        MainMenu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(Editing_MenuOpened), true);
        CommandManager.RequerySuggested += Editing_RequerySuggested;
        UpdateEditingMenus();
    }

    private void Editing_FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindEditingTarget(e.NewFocus as DependencyObject) is { } target) lastEditingTarget = target;
    }

    private IInputElement? FindEditingTarget(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is TextBox box && ReferenceEquals(Window.GetWindow(box), this)) return box;
            if (node is TextArea area && ReferenceEquals(area, Editor.TextArea)) return area;
            node = node is Visual visual ? VisualTreeHelper.GetParent(visual) ?? LogicalTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private IInputElement EditingCommandTarget()
    {
        if (lastEditingTarget is UIElement element && element.IsEnabled && element.Visibility == Visibility.Visible &&
            (!IsVisible || element.IsVisible)) return lastEditingTarget;
        return Editor.TextArea;
    }

    private bool CanExecuteEditingCommand(RoutedCommand command)
    {
        var target = EditingCommandTarget();
        // The log document is read-only; opening clipboard logs remains a separate action.
        if (command == ApplicationCommands.Paste && ReferenceEquals(target, Editor.TextArea) && Editor.IsReadOnly) return false;
        try { return command.CanExecute(null, target); }
        catch (ExternalException) { return false; }
    }

    private void ExecuteEditingCommand(RoutedCommand command)
    {
        if (!CanExecuteEditingCommand(command)) return;
        try { command.Execute(null, EditingCommandTarget()); }
        catch (ExternalException) { OperationStatus.Text = "클립보드 사용 중 · 입력칸을 선택하고 다시 시도하세요."; }
        UpdateEditingMenus();
    }

    private void FocusCopy_Click(object sender, RoutedEventArgs e) => ExecuteEditingCommand(ApplicationCommands.Copy);
    private void FocusPaste_Click(object sender, RoutedEventArgs e) => ExecuteEditingCommand(ApplicationCommands.Paste);
    private void FocusSelectAll_Click(object sender, RoutedEventArgs e) => ExecuteEditingCommand(ApplicationCommands.SelectAll);
    private void Editing_MenuOpened(object sender, RoutedEventArgs e) => UpdateEditingMenus();
    private void Editing_RequerySuggested(object? sender, EventArgs e) => UpdateEditingMenus();

    private void UpdateEditingMenus()
    {
        FocusCopyMenu.IsEnabled = CanExecuteEditingCommand(ApplicationCommands.Copy);
        FocusPasteMenu.IsEnabled = CanExecuteEditingCommand(ApplicationCommands.Paste);
        FocusSelectAllMenu.IsEnabled = CanExecuteEditingCommand(ApplicationCommands.SelectAll);
    }

    private void DisposeEditing()
    {
        RemoveHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(Editing_FocusChanged));
        MainMenu.RemoveHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(Editing_MenuOpened));
        CommandManager.RequerySuggested -= Editing_RequerySuggested;
        lastEditingTarget = null;
    }
}
