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

    /// <summary>复制当前过滤后的汇总表为 TSV（粘贴到 WPS/Excel 自动分列）。</summary>
    private void OnCopyTable(object sender, RoutedEventArgs e)
    {
        var rows = StatsGrid.ItemsSource as System.Collections.IList;
        if (rows == null || rows.Count == 0)
        {
            MessageBox.Show("当前范围内没有可复制的任务。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("任务名\t专注次数\t总时长\t平均单次\t最近一次日期");
        foreach (StatRow r in rows)
            sb.AppendLine($"{Clean(r.Task)}\t{r.Count}\t{Clean(r.TotalText)}\t{Clean(r.AvgText)}\t{Clean(r.LastDate)}");
        Clipboard.SetText(sb.ToString());
        CopyStatus.Text = $"已复制 {rows.Count} 个任务到剪贴板";
    }

    private static string Clean(string? s)
        => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

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
