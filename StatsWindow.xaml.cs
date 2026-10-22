using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskbarTimer.Models;
using TaskbarTimer.Services;

namespace TaskbarTimer;

/// <summary>任务统计窗口：按任务名汇总专注次数与总时长，支持时间范围与关键字过滤。</summary>
public partial class StatsWindow : Window
{
    private readonly List<TimingRecord> _records;
    private List<StatRow> _rangeRows = new();

    public StatsWindow()
    {
        InitializeComponent();
        _records = new RecordStore().GetAll();
        RangeCombo.SelectedIndex = 0; // 默认「全部」，触发 Rebuild + ApplyFilter
    }

    private void OnRangeChanged(object sender, SelectionChangedEventArgs e) => Rebuild();

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    /// <summary>按当前时间范围重新分组聚合，再套用关键字过滤。</summary>
    private void Rebuild()
    {
        _rangeRows = FilterByRange()
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

    private void ApplyFilter()
    {
        var kw = SearchBox?.Text.Trim() ?? "";
        IEnumerable<StatRow> rows = _rangeRows;
        if (kw.Length > 0)
            rows = _rangeRows.Where(r => r.Task.Contains(kw, StringComparison.OrdinalIgnoreCase));

        var list = rows.ToList();
        if (StatsGrid != null) StatsGrid.ItemsSource = list;

        if (SummaryText != null)
        {
            var total = TimeSpan.FromSeconds(list.Sum(r => r.TotalSeconds));
            SummaryText.Text = $"共 {list.Count} 个任务 · {list.Sum(r => r.Count)} 次专注 · 累计 {TimerWidget.Format(total)}";
        }
    }

    /// <summary>按时间范围过滤原始记录（Date 为 yyyy-MM-dd 字符串，字典序即时间序）。</summary>
    private IEnumerable<TimingRecord> FilterByRange()
    {
        var today = DateTime.Today;
        string d(DateTime dt) => dt.ToString("yyyy-MM-dd");
        return (RangeCombo?.SelectedIndex ?? 0) switch
        {
            1 => _records.Where(r => r.Date == d(today)),                            // 今天
            2 => _records.Where(r => r.Date == d(today.AddDays(-1))),                // 昨天
            3 => _records.Where(r => r.Date.CompareTo(d(today.AddDays(-6))) >= 0),   // 近7天
            4 => _records.Where(r => r.Date.CompareTo(d(today.AddDays(-29))) >= 0),  // 近30天
            _ => _records,                                                           // 全部
        };
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
