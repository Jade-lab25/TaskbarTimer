using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskbarTimer.Services;

namespace TaskbarTimer;

/// <summary>任务统计窗口：按任务名汇总专注次数与总时长，支持关键字过滤。</summary>
public partial class StatsWindow : Window
{
    private readonly List<StatRow> _allRows;

    public StatsWindow()
    {
        InitializeComponent();

        var records = new RecordStore().GetAll();
        _allRows = records
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Task) ? "(未命名)" : r.Task.Trim())
            .Select(g => new StatRow
            {
                Task = g.Key,
                Count = g.Count(),
                TotalSeconds = g.Sum(r => r.DurationSeconds),
                LastDate = g.Max(r => r.Date),
            })
            .OrderByDescending(x => x.TotalSeconds)
            .ToList();

        ApplyFilter();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var kw = SearchBox?.Text.Trim() ?? "";
        IEnumerable<StatRow> rows = _allRows;
        if (kw.Length > 0)
            rows = _allRows.Where(r => r.Task.Contains(kw, StringComparison.OrdinalIgnoreCase));

        var list = rows.ToList();
        StatsGrid.ItemsSource = list;

        if (SummaryText != null)
        {
            var total = TimeSpan.FromSeconds(list.Sum(r => r.TotalSeconds));
            SummaryText.Text = $"共 {list.Count} 个任务 · {list.Sum(r => r.Count)} 次专注 · 累计 {TimerWidget.Format(total)}";
        }
    }

    /// <summary>双击任务行 → 把任务名填回主窗口输入框，方便直接再次计时。</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StatsGrid.SelectedItem is not StatRow row) return;
        var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main == null) return;
        main.SetTaskInput(row.Task);
        main.Activate();
    }
}

/// <summary>汇总行视图模型。</summary>
public class StatRow
{
    public string Task { get; init; } = "";
    public int Count { get; init; }
    public double TotalSeconds { get; init; }
    public string TotalText => TimerWidget.Format(TimeSpan.FromSeconds(TotalSeconds));
    public string AvgText => TimerWidget.Format(TimeSpan.FromSeconds(Count > 0 ? TotalSeconds / Count : 0));
    public string LastDate { get; init; } = "";
}
