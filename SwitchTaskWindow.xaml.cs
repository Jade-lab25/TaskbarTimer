using System.Windows;
using System.Windows.Input;

namespace TaskbarTimer;

/// <summary>切换任务极简小窗：输入框（回车确认）+ 最近 8 个历史任务快捷按钮。</summary>
public partial class SwitchTaskWindow : Window
{
    /// <summary>用户确认后的新任务名（取消时为 null）。</summary>
    public string? SelectedTask { get; private set; }

    public SwitchTaskWindow(IEnumerable<string> recentTasks)
    {
        InitializeComponent();
        RecentList.ItemsSource = recentTasks;
        Loaded += (_, _) =>
        {
            TaskInput.Focus();
            TaskInput.SelectAll();
        };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Confirm(TaskInput.Text);
        else if (e.Key == Key.Escape) Close();
    }

    private void OnRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Content is string t)
            Confirm(t);
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => Confirm(TaskInput.Text);

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void Confirm(string? raw)
    {
        var task = raw?.Trim() ?? "";
        if (task.Length == 0)
        {
            TaskInput.Focus();
            return;
        }
        SelectedTask = task;
        DialogResult = true;
    }
}
