using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private string ThreadSearchQuery
    {
        get => ThreadSearchBox.Text;
        set => ThreadSearchBox.Text = value ?? "";
    }

    private void ThreadSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (viewReady) RefreshThreadSearch();
    }

    private void ClearThreadSearch_Click(object sender, RoutedEventArgs e)
    {
        ThreadSearchBox.Clear();
        ThreadSearchBox.Focus();
    }

    private void RefreshThreadSearch()
    {
        if (ThreadSearchBox is null || ThreadSearchStatus is null || ThreadList is null) return;
        string query = ThreadSearchQuery.Trim();
        string compact = query.Replace(" ", "", StringComparison.Ordinal);
        var view = CollectionViewSource.GetDefaultView(threadItems);
        view.Filter = query.Length == 0 ? null : item => item is ThreadItem thread && MatchesThreadSearch(thread, query, compact);
        ThreadList.ItemsSource = view;
        view.Refresh();
        ThreadSearchClearButton.IsEnabled = ThreadSearchQuery.Length > 0;
        ThreadSearchStatus.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (query.Length == 0) { ThreadSearchStatus.Text = ""; return; }
        int matches = view.Cast<ThreadItem>().Count();
        ThreadSearchStatus.Text = matches == 0 ? "일치하는 스레드가 없습니다. 찾기를 지우면 전체 목록을 봅니다." :
            $"목록 찾기: {matches:N0}/{threadItems.Count:N0}개 · 로그 필터는 유지됩니다.";
    }

    private static bool MatchesThreadSearch(ThreadItem thread, string query, string compact)
    {
        if (thread.Id is not { } id) return "미분류".Contains(query, StringComparison.OrdinalIgnoreCase);
        string number = id.ToString(CultureInfo.InvariantCulture);
        return number.Contains(compact, StringComparison.OrdinalIgnoreCase) ||
            ("T" + number).Contains(compact, StringComparison.OrdinalIgnoreCase);
    }
}
