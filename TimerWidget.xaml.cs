using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarTimer.Models;
using TaskbarTimer.Services;

namespace TaskbarTimer;

/// <summary>
/// 任务栏贴靠计时小窗：无边框置顶，默认出现在任务栏时钟左侧，可拖动记忆位置。
/// 左键单击：暂停/继续；双击：打开主窗口；右键：菜单。
/// </summary>
public partial class TimerWidget : Window
{
    private readonly SessionService _session = SessionService.Instance;
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherTimer _topmostKeeper;
    private readonly DispatcherTimer _flashTimer;
    private bool _flashOn;
    private DispatcherTimer? _checkInTimer; // 巡检 5 秒超时
    private DispatcherTimer? _goalTimer;    // 目标达成提示 3 秒
    private bool _showGoalReached;
    private double _preCheckInTop;           // 巡检展开前窗口 Top，用于收起后复位
    private bool _shiftedForCheckIn;         // 巡检展开时是否已上移窗口

    public TimerWidget()
    {
        InitializeComponent();
        RestorePosition();
        Hide();

        // 每 2 秒重申置顶，防止被全屏窗口压下去
        _topmostKeeper = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostKeeper.Tick += (_, _) => { if (Visibility == Visibility.Visible) Topmost = true; };
        _topmostKeeper.Start();

        // 倒计时归零后的红黑闪烁
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashOn = !_flashOn;
            RootBorder.Background = _flashOn
                ? (Brush)FindResource("ColorDanger")
                : (Brush)FindResource("ColorBgDark");
        };

        _session.Tick += UpdateDisplay;
        _session.StateChanged += OnStateChanged;
        _session.Finished += OnFinished;
        _session.CheckInDue += OnCheckInDue;
        _session.GoalReached += OnGoalReached;
        _session.DistractionChanged += OnDistractionChanged;
        Closed += (_, _) =>
        {
            _session.Tick -= UpdateDisplay;
            _session.StateChanged -= OnStateChanged;
            _session.Finished -= OnFinished;
            _session.CheckInDue -= OnCheckInDue;
            _session.GoalReached -= OnGoalReached;
            _session.DistractionChanged -= OnDistractionChanged;
        };
    }

    // ---------- 显示逻辑 ----------

    private void UpdateDisplay()
    {
        var seconds = _session.Mode == TimerMode.CountDown
            ? _session.RemainingSeconds
            : _session.ElapsedSeconds;
        TimeText.Text = Format(TimeSpan.FromSeconds(seconds));
        NameText.Text = Truncate(_session.TaskName, 14);
        UpdateGoalDisplay();
    }

    /// <summary>更新正计时目标：进度细线 + 「已过/目标」分钟数（或「目标达成 ✓」）。</summary>
    private void UpdateGoalDisplay()
    {
        bool hasGoal = _session.Mode == TimerMode.CountUp && _session.TargetSeconds > 0;
        if (_showGoalReached)
        {
            GoalText.Text = "目标达成 ✓";
            GoalText.Foreground = (Brush)FindResource("ColorSuccess");
            GoalText.Visibility = Visibility.Visible;
        }
        else if (hasGoal)
        {
            int elapsedMin = (int)Math.Floor(_session.ElapsedSeconds / 60);
            int targetMin = (int)Math.Round(_session.TargetSeconds / 60);
            GoalText.Text = $"{elapsedMin}/{targetMin}";
            GoalText.Foreground = (Brush)FindResource("ColorTextMuted");
            GoalText.Visibility = Visibility.Visible;
        }
        else
        {
            GoalText.Visibility = Visibility.Collapsed;
        }
        GoalProgress.Visibility = hasGoal ? Visibility.Visible : Visibility.Collapsed;
        if (hasGoal)
            GoalProgress.Value = Math.Min(100, _session.ElapsedSeconds / _session.TargetSeconds * 100);
    }

    private void OnStateChanged()
    {
        if (_session.IsIdle)
        {
            CloseCheckIn();
            _showGoalReached = false;
            Hide();
            return;
        }
        if (!IsVisible) Show(); // 仅在隐藏时才显示，避免暂停/继续时重复 Show 抢焦点、打断双击
        if (_session.State != SessionState.Running) CloseCheckIn(); // 暂停时收起巡检
        _flashTimer.Stop();
        RootBorder.Background = (Brush)FindResource("ColorBgDark");
        TimeText.Foreground = (Brush)FindResource("ColorTextLight");

        var paused = _session.State == SessionState.Paused;
        // 暂停时时间数字变灰，提供明确视觉反馈
        TimeText.Foreground = paused
            ? (Brush)FindResource("ColorTextMuted")
            : (Brush)FindResource("ColorTextLight");
        NameText.Text = paused ? "已暂停" : Truncate(_session.TaskName, 14);
        UpdateDistractionMenu();
        UpdateDisplay();
    }

    private void OnFinished()
    {
        _flashTimer.Start();
        if (_settingsStore.Get().PlaySoundOnFinish) System.Media.SystemSounds.Exclamation.Play();
    }

    // ---------- 注意力巡检 ----------

    private void OnCheckInDue()
    {
        if (_session.State != SessionState.Running) return;
        CheckInText.Text = $"还在做【{Truncate(_session.TaskName, 12)}】吗？";
        CheckInPanel.Visibility = Visibility.Visible;
        StartCheckInTimeout();
        // 巡检行向下展开后，若窗口底部超出工作区（贴任务栏时会被任务栏挡住），整体上移露出巡检行
        Dispatcher.BeginInvoke(new Action(ShiftToRevealCheckIn), DispatcherPriority.Loaded);
    }

    /// <summary>布局完成后，若窗口底部越出工作区则整体上移，让巡检行完整可见。</summary>
    private void ShiftToRevealCheckIn()
    {
        if (CheckInPanel.Visibility != Visibility.Visible) return;
        UpdateLayout(); // 强制同步布局，确保 SizeToContent 已按巡检行更新窗口高度
        var wa = SystemParameters.WorkArea;
        if (Top + ActualHeight > wa.Bottom)
        {
            _preCheckInTop = Top;
            _shiftedForCheckIn = true;
            Top = wa.Bottom - ActualHeight - 4;
        }
    }

    private void StartCheckInTimeout()
    {
        _checkInTimer?.Stop();
        _checkInTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _checkInTimer.Tick += (_, _) =>
        {
            CloseCheckIn();
            _session.ResolveCheckIn(false); // 超时：不记录、不惩罚，重排下一次
        };
        _checkInTimer.Start();
    }

    private void CloseCheckIn()
    {
        _checkInTimer?.Stop();
        CheckInPanel.Visibility = Visibility.Collapsed;
        // 恢复巡检展开时的上移，让窗口回到贴任务栏的原位
        if (_shiftedForCheckIn)
        {
            _shiftedForCheckIn = false;
            Top = _preCheckInTop;
        }
    }

    private void OnCheckInFocus(object sender, RoutedEventArgs e)
    {
        CloseCheckIn();
        _session.ResolveCheckIn(false);
    }

    private void OnCheckInDistracted(object sender, RoutedEventArgs e)
    {
        CloseCheckIn();
        _session.ResolveCheckIn(true);
    }

    // ---------- 正计时目标达成 ----------

    private void OnGoalReached()
    {
        System.Media.SystemSounds.Asterisk.Play();
        _showGoalReached = true;
        UpdateGoalDisplay();
        _goalTimer?.Stop();
        _goalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _goalTimer.Tick += (_, _) =>
        {
            _showGoalReached = false;
            _goalTimer.Stop();
            UpdateGoalDisplay();
        };
        _goalTimer.Start();
    }

    private void OnDistractionChanged() => UpdateDistractionMenu();

    private void UpdateDistractionMenu()
        => MenuDistraction.Header = $"我分心了（{_session.DistractionCount}）";

    internal static string Format(TimeSpan t)
        => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                             : $"{t.Minutes:D2}:{t.Seconds:D2}";

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

    // ---------- 交互（按下记录 → 移动拖拽 → 松开判定单击/双击） ----------

    private Point _downPos;
    private bool _leftDown;
    private bool _wasDragged;
    private bool _suppressUpClick; // 双击时抑制第二次松手的单击判定
    private DateTime _lastUpTime = DateTime.MinValue;
    private const double DragThreshold = 6;

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 双击：第二次按下（距上次松手在系统双击时间内）→ 撤销第一次单击的 Toggle，并打开主窗口
        if ((DateTime.Now - _lastUpTime).TotalMilliseconds < GetDoubleClickTime())
        {
            _session.Toggle();      // 撤销第一次单击造成的暂停/继续
            _suppressUpClick = true;
            _leftDown = false;
            AppActions.ShowMain();
            e.Handled = true;
            return;
        }
        _leftDown = true;
        _wasDragged = false;
        _downPos = e.GetPosition(this);
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_leftDown) return;
        var p = e.GetPosition(this);
        if (!_wasDragged && (Math.Abs(p.X - _downPos.X) > DragThreshold || Math.Abs(p.Y - _downPos.Y) > DragThreshold))
        {
            _wasDragged = true;
            _leftDown = false;
            // 拖动时若巡检展开中，先收起并重排，避免窗口位置错乱
            if (CheckInPanel.Visibility == Visibility.Visible)
            {
                CloseCheckIn();
                _session.ResolveCheckIn(false);
            }
            try { DragMove(); } catch { }
            PersistPosition();
        }
    }

    private void OnMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _leftDown = false;
        if (_wasDragged) return;
        if (_suppressUpClick) { _suppressUpClick = false; return; }
        // 单击（未拖动）：立即切换暂停/继续
        _session.Toggle();
        _lastUpTime = DateTime.Now;
    }

    private void OnMenuToggle(object sender, RoutedEventArgs e) => _session.Toggle();

    private void OnMenuSwitch(object sender, RoutedEventArgs e) => SwitchTask();

    private void OnMenuDistraction(object sender, RoutedEventArgs e) => _session.RecordDistraction();

    private void OnMenuStop(object sender, RoutedEventArgs e) => AppActions.StopSession();

    private void OnMenuMain(object sender, RoutedEventArgs e) => AppActions.ShowMain();

    private void OnMenuExit(object sender, RoutedEventArgs e) => AppActions.ExitApp(this);

    /// <summary>弹出切换任务小窗，确认后原子切换（保存当前 → 立即开始新任务）。</summary>
    private void SwitchTask()
    {
        if (_session.IsIdle) return;
        var recent = new RecordStore().RecentTaskNames(8);
        var dlg = new SwitchTaskWindow(recent);
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.SelectedTask))
            _session.SwitchTask(dlg.SelectedTask);
    }

    private void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (_session.IsIdle) { e.Handled = true; return; }
        var mode = _session.Mode == TimerMode.CountDown ? "倒计时" : "正计时";
        var state = _session.State == SessionState.Running ? "进行中" : "已暂停";
        WidgetTip.Content = $"{_session.TaskName}\n{mode} · {state} · 开始于 {_session.StartedAt:HH:mm}";
    }

    // ---------- 位置记忆 ----------

    private void RestorePosition()
    {
        var s = _settingsStore.Get();
        if (IsVisibleOnScreen(s.WidgetX, s.WidgetY))
        {
            Left = s.WidgetX;
            Top = s.WidgetY;
        }
        else
        {
            MoveToDefault();
        }
    }

    /// <summary>判断某坐标是否完整落在主屏工作区内（小窗不会被任务栏或屏幕边缘遮住）。</summary>
    private bool IsVisibleOnScreen(double x, double y)
    {
        var wa = SystemParameters.WorkArea;
        double w = Math.Max(ActualWidth, 160);
        double h = Math.Max(ActualHeight, 30);
        return !double.IsNaN(x) && !double.IsNaN(y)
            && x >= wa.Left && y >= wa.Top
            && x + w <= wa.Right && y + h <= wa.Bottom;
    }

    /// <summary>移动到默认位置：主屏工作区右下角、任务栏正上方、系统时钟托盘区左侧。</summary>
    private void MoveToDefault()
    {
        var wa = SystemParameters.WorkArea;
        double w = Math.Max(ActualWidth, 160);
        double h = Math.Max(ActualHeight, 30);
        Left = wa.Right - w - 200;
        Top = wa.Bottom - h - 4;
    }

    /// <summary>把小窗拉回默认位置（供主窗口「重置小窗位置」调用）。</summary>
    public void ResetPosition()
    {
        MoveToDefault();
        PersistPosition();
        if (Visibility != Visibility.Visible && !_session.IsIdle) Show();
    }

    private void PersistPosition()
    {
        // 保存前先钳制到工作区内，防止再次被拖出屏幕
        var wa = SystemParameters.WorkArea;
        double w = Math.Max(ActualWidth, 160);
        double h = Math.Max(ActualHeight, 30);
        double x = Math.Clamp(Left, wa.Left, wa.Right - w);
        double y = Math.Clamp(Top, wa.Top, wa.Bottom - h);
        Left = x;
        Top = y;
        var s = _settingsStore.Get();
        s.WidgetX = x;
        s.WidgetY = y;
        _settingsStore.Save(s);
    }
}

/// <summary>跨窗口动作集合（小窗菜单与主窗口共用）。</summary>
public static class AppActions
{
    public static void ShowMain()
    {
        // 注意：不能依赖 Application.MainWindow（默认指向第一个实例化的窗口，即小窗自己）
        var main = Application.Current.MainWindow as MainWindow
            ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
        if (main == null) return;
        main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        // 强制置前：临时置顶一次，确保主窗口从任务栏/背后被唤起时有明确反馈
        main.Topmost = true;
        main.Activate();
        main.Topmost = false;
    }

    /// <summary>静默结束并保存当前会话（不弹「已保存」提示框，避免系统提示音）。</summary>
    public static TimingRecord? StopSession()
    {
        return SessionService.Instance.StopAndSave();
    }

    public static void ExitApp(Window sender)
    {
        if (!SessionService.Instance.IsIdle)
        {
            var choice = MessageBox.Show("当前有计时进行中，退出将不保存本次记录。确定退出？",
                "退出确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes) return;
        }
        sender.Close();
        Application.Current.Shutdown();
    }
}
