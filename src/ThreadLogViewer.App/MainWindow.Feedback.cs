using System.Windows;
using ThreadLogViewer.Core;

namespace ThreadLogViewer.App;

public partial class MainWindow
{
    private string workStartingStatus = "", workLabel = "";
    private bool HasContentFilter => appliedFilter.Includes.Length > 0 || appliedFilter.Excludes.Length > 0;

    private void UpdateEmptyResultState()
    {
        if (EmptyClearContentButton is null || EmptyClearAllButton is null) return;
        bool empty = projection?.Count == 0 && data?.Lines.Count > 0 && !IsBlankSession;
        bool noThreads = projection?.SelectedThreads.Count == 0;
        EmptyClearContentButton.Visibility = empty && HasContentFilter ? Visibility.Visible : Visibility.Collapsed;
        EmptyClearAllButton.Visibility = empty && (HasContentFilter || noThreads) ? Visibility.Visible : Visibility.Collapsed;
        EmptyClearContentButton.IsEnabled = EmptyClearAllButton.IsEnabled = !busy && !contextActive;
        if (data is null || IsBlankSession) return;
        if (data.Lines.Count == 0)
        {
            EmptyHint.Text = "빈 파일입니다.";
            EmptyDetail.Text = "다른 파일을 열거나 Ctrl+V로 로그를 붙여넣으세요.";
        }
        else if (noThreads)
        {
            EmptyHint.Text = "선택한 스레드가 없습니다.";
            EmptyDetail.Text = HasContentFilter
                ? "스레드 선택과 내용 필터가 함께 적용되어 있습니다. 모든 필터를 해제하면 전체 기록을 표시합니다."
                : "왼쪽에서 스레드를 선택하거나 ‘모든 필터 해제’를 누르세요.";
        }
        else if (HasContentFilter)
        {
            EmptyHint.Text = "내용 필터에 맞는 기록이 없습니다.";
            EmptyDetail.Text = "‘내용 필터 해제’는 스레드 선택을 유지합니다. ‘모든 필터 해제’는 전체 기록을 표시합니다.";
        }
        else
        {
            EmptyHint.Text = "표시할 기록이 없습니다.";
            EmptyDetail.Text = "현재 세션의 스레드 선택을 확인하세요.";
        }
    }

    private async void ResetAllFilters_Click(object sender, RoutedEventArgs e) => await ResetAllFiltersAsync();
    private async Task ResetAllFiltersAsync()
    {
        if (busy || data is null || IsBlankSession) return;
        IncludeBox.Clear(); ExcludeBox.Clear(); IncludeModeBox.SelectedIndex = 0; FilterCaseBox.IsChecked = false;
        suppressFilters = true;
        foreach (var item in threadItems) item.IsSelected = true;
        suppressFilters = false;
        await FilterAsync(EntryFilter.Empty);
        UpdateFilterDraftStatus();
    }
}
