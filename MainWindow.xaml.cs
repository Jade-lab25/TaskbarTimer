using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using TaskbarTimer.Models;
using TaskbarTimer.Services;

namespace TaskbarTimer;

/// <summary>主窗口：发起计时 + 每日统计与 CSV 导出。</summary>
public partial class MainWindow : Window
{
    private readonly SessionService _session = SessionService.Instance;
    private readonly RecordStore _store = new();
    private readonly SettingsStore _settingsStore = new();
    private ObservableCollection<RecordRow> _rows = new();
    private bool _syncingNotes; // 程序回填备忘时置位，防止触发 TextChanged 回写循环
    private bool _settingsReady; // 巡检设置控件初始化完成前，忽略设置变更回写

    public MainWindow()
    {
        InitializeComponent();
        _session.StateChanged += OnSessionChanged;
        _session.Tick += OnSessionTick;
        _session.DistractionChanged += OnDistractionChanged;
        Closed += (_, _) =>
        {
            _session.StateChanged -= OnSessionChanged;
            _session.Tick -= OnSessionTick;
            _session.DistractionChanged -= OnDistractionChanged;
        };
        RangeCombo.SelectedIndex = 0; // 在 InitializeComponent 完成后设置，触发首次加载
        LoadCheckInSettings();
        _settingsReady = true;
        OnSessionChanged();

        // 倒计时归零 → 任务栏图标闪烁提醒
        _session.Finished += OnSessionFinished;
        Closed += (_, _) => _session.Finished -= OnSessionFinished;
    }

    private void OnSessionFinished() => Dispatcher.Invoke(FlashTaskbarIcon);

    // ---------- 任务栏图标闪烁（FlashWindowEx） ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    private const uint FLASHW_ALL = 0x3;          // 标题栏 + 任务栏按钮一起闪
    private const uint FLASHW_TIMERNOFG = 0xC;    // 持续闪烁，直到窗口来到前台

    /// <summary>任务栏图标持续闪烁，用户点开窗口后自动停止。</summary>
    private void FlashTaskbarIcon()
    {
        if (!IsVisible)
        {
            Show(); // 主窗口被关闭隐藏时，先恢复到任务栏（最小化），保证有图标可闪
            WindowState = WindowState.Minimized;
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        FlashWindowEx(ref info);
    }

    // ---------- 计时 ----------

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!_session.IsIdle)
        {
            var r = MessageBox.Show("当前已有计时进行中，结束并保存后才能开始新计时。现在结束吗？",
                "提示", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            _session.StopAndSave();
        }

        var task = TaskInput.Text.Trim();
        if (task.Length == 0)
        {
            MessageBox.Show("请先填写任务名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            TaskInput.Focus();
            return;
        }

        double planned = 0;
        if (ModeDown.IsChecked == true)
        {
            if (!double.TryParse(MinutesInput.Text, out var minutes) || minutes <= 0)
            {
                MessageBox.Show("计划时长必须是大于 0 的数字（分钟）。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MinutesInput.Focus();
                return;
            }
            planned = minutes * 60;
        }

        // 正计时目标时长（0=不设目标）
        double target = 0;
        if (ModeUp.IsChecked == true)
        {
            foreach (var rb in TargetPanel.Children.OfType<RadioButton>())
            {
                if (rb.IsChecked == true && rb.Tag is string tag && double.TryParse(tag, out var tmin))
                {
                    target = tmin * 60;
                    break;
                }
            }
        }

        _session.Start(ModeDown.IsChecked == true ? TimerMode.CountDown : TimerMode.CountUp,
            task, planned, target, NotesInput.Text);
        WindowState = WindowState.Minimized; // 计时开始后缩到任务栏，让位给工作
    }

    private void OnStop(object sender, RoutedEventArgs e)
    {
        var r = AppActions.StopSession();
        if (r != null)
            LiveStatus.Text = $"已保存：{r.Task} · {TimeSpan.FromSeconds(r.DurationSeconds):hh\\:mm\\:ss}";
    }

    private void OnTaskInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnStart(sender, e);
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (CountdownPanel == null || TargetPanel == null) return;
        bool down = ModeDown.IsChecked == true;
        CountdownPanel.Visibility = down ? Visibility.Visible : Visibility.Collapsed;
        TargetPanel.Visibility = down ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b) MinutesInput.Text = b.Content.ToString();
    }

    private void OnSessionChanged()
    {
        Dispatcher.Invoke(() =>
        {
            bool active = !_session.IsIdle;
            StartBtn.IsEnabled = !active;
            StopBtn.IsEnabled = active;
            SwitchBtn.IsEnabled = active;
            DistractionBtn.IsEnabled = active;
            UpdateDistractionButton();
            if (active)
            {
                var mode = _session.Mode == TimerMode.CountDown ? "倒计时" : "正计时";
                LiveStatus.Text = $"进行中：{_session.TaskName}（{mode}）";
                _syncingNotes = true;
                NotesInput.Text = _session.Notes;
                _syncingNotes = false;
            }
            else
            {
                LiveStatus.Text = "";
                LoadRecords(); // 刚结束的会话已落库，刷新列表
                // 会话结束，清空备忘草稿，回到待开始状态
                _syncingNotes = true;
                NotesInput.Text = "";
                _syncingNotes = false;
            }
        });
    }

    private void OnSessionTick()
    {
        Dispatcher.Invoke(() =>
        {
            if (_session.IsIdle) return;
            var t = _session.Mode == TimerMode.CountDown
                ? TimeSpan.FromSeconds(_session.RemainingSeconds)
                : TimeSpan.FromSeconds(_session.ElapsedSeconds);
            LiveStatus.Text = $"进行中：{_session.TaskName} · {TimerWidget.Format(t)}" +
                (_session.State == SessionState.Paused ? "（已暂停）" : "");
        });
    }

    /// <summary>备忘输入实时写回会话（_syncingNotes 防程序回填触发循环）；未计时时仅作草稿，开始计时时带入。</summary>
    private void OnNotesChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingNotes) return;
        if (!_session.IsIdle) _session.SetNotes(NotesInput.Text);
    }

    /// <summary>拖动右下角斜纹手柄调整备忘输入框高度。</summary>
    private void OnNotesResizeDragDelta(object sender, DragDeltaEventArgs e)
    {
        var newH = NotesInput.Height + e.VerticalChange;
        if (newH < NotesInput.MinHeight) newH = NotesInput.MinHeight;
        if (newH > NotesInput.MaxHeight) newH = NotesInput.MaxHeight;
        NotesInput.Height = newH;
    }

    // ---------- 切换任务 / 分心计数 ----------

    private void OnSwitchTask(object sender, RoutedEventArgs e)
    {
        if (_session.IsIdle) return;
        var recent = new RecordStore().RecentTaskNames(8);
        var dlg = new SwitchTaskWindow(recent);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.SelectedTask))
        {
            _session.SwitchTask(dlg.SelectedTask);
            TaskInput.Text = dlg.SelectedTask;
        }
    }

    private void OnDistraction(object sender, RoutedEventArgs e) => _session.RecordDistraction();

    private void OnDistractionChanged() => UpdateDistractionButton();

    private void UpdateDistractionButton()
        => DistractionBtn.Content = $"我分心了（{_session.DistractionCount}）";

    // ---------- 注意力巡检设置 ----------

    private void LoadCheckInSettings()
    {
        var s = _settingsStore.Get();
        CheckInEnabled.IsChecked = s.CheckInEnabled;
        CheckInMin.Text = (s.CheckInMinSeconds / 60.0).ToString("0.##");
        CheckInMax.Text = (s.CheckInMaxSeconds / 60.0).ToString("0.##");
    }

    private void OnCheckInEnabledClick(object sender, RoutedEventArgs e) => SaveCheckInSettings();

    private void OnCheckInMinutesChanged(object sender, TextChangedEventArgs e) => SaveCheckInSettings();

    private void SaveCheckInSettings()
    {
        if (!_settingsReady) return;
        var s = _settingsStore.Get();
        s.CheckInEnabled = CheckInEnabled.IsChecked == true;
        if (double.TryParse(CheckInMin.Text, out var min) && min > 0)
            s.CheckInMinSeconds = (int)Math.Round(min * 60);
        if (double.TryParse(CheckInMax.Text, out var max) && max > 0)
            s.CheckInMaxSeconds = (int)Math.Round(max * 60);
        _settingsStore.Save(s);
        _session.RefreshSettings();
    }

    // ---------- 统计 ----------

    private void OnRangeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => LoadRecords();

    private void LoadRecords()
    {
        var all = _store.GetAll().OrderByDescending(r => r.Date).ThenByDescending(r => r.Start).ToList();
        IEnumerable<string>? range = (RangeCombo?.SelectedIndex ?? 0) switch
        {
            0 => new[] { DateTime.Today.ToString("yyyy-MM-dd") },
            1 => Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd")).ToHashSet(),
            _ => null,
        };
        var filtered = range == null ? all : all.Where(r => range.Contains(r.Date)).ToList();

        _rows = new ObservableCollection<RecordRow>(filtered.Select(r => new RecordRow(r, _store)));
        if (RecordsGrid != null) RecordsGrid.ItemsSource = _rows;

        // 汇总：今天 N 次 / 共 X 分钟 / 分心 N 次（跨全部记录中的今日数据）
        var (count, seconds, distractions) = _store.DailySummary(DateTime.Today.ToString("yyyy-MM-dd"));
        if (SummaryText != null)
            SummaryText.Text = $"今日专注 {count} 次，共 {seconds / 60.0:F0} 分钟，今日分心 {distractions} 次" +
                $"（列表 {filtered.Count} 条）";
    }

    private void OnResetWidget(object sender, RoutedEventArgs e)
    {
        App.Widget?.ResetPosition();
        if (!_session.IsIdle) App.Widget?.Show();
    }

    /// <summary>复用选中历史记录的任务名。</summary>
    private void OnReuseTask(object sender, RoutedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not RecordRow row)
        {
            MessageBox.Show("请先在列表中选中一条历史记录。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        TaskInput.Text = row.Task;
        TaskInput.Focus();
        TaskInput.SelectAll();
    }

    /// <summary>双击历史记录行 → 填充任务名，方便直接开始。</summary>
    private void OnRecordsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // 双击「进展记录」「专注事项」列是进入单元格编辑，不填充任务名
        var header = RecordsGrid.CurrentColumn?.Header.ToString();
        if (header == "进展记录" || header == "专注事项") return;
        if (RecordsGrid.SelectedItem is RecordRow row)
        {
            TaskInput.Text = row.Task;
            TaskInput.Focus();
        }
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (RecordsGrid.SelectedItem is not RecordRow row) return;
        if (MessageBox.Show($"删除记录「{row.Task} {row.Date} {row.Start}」？", "确认",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        row.Delete(_store);
        LoadRecords();
    }

    // ---------- 任务统计 ----------

    private StatsWindow? _statsWin;

    private void OnStats(object sender, RoutedEventArgs e)
    {
        if (_statsWin != null && _statsWin.IsVisible)
        {
            _statsWin.Activate();
            return;
        }
        _statsWin = new StatsWindow { Owner = this };
        _statsWin.Closed += (_, _) => _statsWin = null;
        _statsWin.Show();
    }

    /// <summary>供统计窗口双击回填任务名。</summary>
    public void SetTaskInput(string task)
    {
        TaskInput.Text = task;
        TaskInput.Focus();
        TaskInput.SelectAll();
    }

    /// <summary>复制当前筛选范围的全部记录为 TSV（粘贴到 WPS/Excel 自动分列）。</summary>
    private void OnCopyTable(object sender, RoutedEventArgs e)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show("当前范围内没有可复制的记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("日期\t开始时间\t结束时间\t时长\t专注事项\t进展记录\t分心次数");
        foreach (var r in _rows)
            sb.AppendLine($"{Clean(r.Date)}\t{Clean(r.Start)}\t{Clean(r.End)}\t{Clean(r.DurationText)}\t{Clean(r.Task)}\t{Clean(r.Notes)}\t{Clean(r.DistractionText)}");
        Clipboard.SetText(sb.ToString());
        LiveStatus.Text = $"已复制 {_rows.Count} 条记录到剪贴板";
    }

    private static string Clean(string? s)
        => (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var records = _rows.Select(r => r.Source).ToList();
        if (records.Count == 0)
        {
            MessageBox.Show("当前范围内没有可导出的记录。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var path = CsvExporter.WriteFile(records,
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (MessageBox.Show($"已导出到桌面：\n{Path.GetFileName(path)}\n\n是否立即打开所在文件夹？",
                "导出成功", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }

    private void OnExit(object sender, RoutedEventArgs e) => AppActions.ExitApp(this);

    // 关闭主窗口 = 最小化隐藏，程序由小窗菜单退出
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}

/// <summary>表格行视图模型：包装 TimingRecord，支持进展记录编辑与删除。</summary>
public class RecordRow : INotifyPropertyChanged
{
    public TimingRecord Source { get; }
    private string _notes;
    private string _task;
    private readonly TimingRecord _record;

    public RecordRow(TimingRecord record, RecordStore store)
    {
        _record = record;
        Source = record;
        _notes = record.Notes;
        _task = record.Task;
    }

    public string Date => _record.Date;
    public string Start => _record.Start;
    public string End => _record.End;
    public string DurationText => TimerWidget.Format(TimeSpan.FromSeconds(_record.DurationSeconds));

    /// <summary>专注事项：可写属性，DataGrid 编辑提交后立即落库。</summary>
    public string Task
    {
        get => _task;
        set
        {
            if (value == _task) return;
            _task = value;
            _record.Task = value;
            new RecordStore().Update(_record);
            OnPropertyChanged(nameof(Task));
        }
    }

    /// <summary>分心次数：0 显示为 -。</summary>
    public string DistractionText
        => _record.DistractionCount > 0 ? _record.DistractionCount.ToString() : "-";

    /// <summary>进展记录：可写属性，DataGrid 编辑提交后立即落库。</summary>
    public string Notes
    {
        get => _notes;
        set
        {
            if (value == _notes) return;
            _notes = value;
            _record.Notes = value;
            new RecordStore().Update(_record);
            OnPropertyChanged(nameof(Notes));
        }
    }

    public void Delete(RecordStore store) => store.Remove(_record.Id);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new(name));
}
