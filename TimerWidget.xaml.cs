using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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
        Closed += (_, _) =>
        {
            _session.Tick -= UpdateDisplay;
            _session.StateChanged -= OnStateChanged;
            _session.Finished -= OnFinished;
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
    }

    private void OnStateChanged()
    {
        if (_session.IsIdle) { Hide(); return; }
        if (!IsVisible) Show(); // 仅在隐藏时才显示，避免暂停/继续时重复 Show 抢焦点、打断双击
        _flashTimer.Stop();
        RootBorder.Background = (Brush)FindResource("ColorBgDark");
        TimeText.Foreground = (Brush)FindResource("ColorTextLight");

        var paused = _session.State == SessionState.Paused;
        // 暂停时时间数字变灰，提供明确视觉反馈
        TimeText.Foreground = paused
            ? (Brush)FindResource("ColorTextMuted")
            : (Brush)FindResource("ColorTextLight");
        NameText.Text = paused ? "已暂停" : Truncate(_session.TaskName, 14);
        UpdateDisplay();
    }

    private void OnFinished()
    {
        _flashTimer.Start();
        if (_settingsStore.Get().PlaySoundOnFinish) System.Media.SystemSounds.Exclamation.Play();
    }

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

    private void OnMenuStop(object sender, RoutedEventArgs e) => AppActions.StopSession();

    private void OnMenuMain(object sender, RoutedEventArgs e) => AppActions.ShowMain();

    private void OnMenuExit(object sender, RoutedEventArgs e) => AppActions.ExitApp(this);

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

    public static void StopSession()
    {
        var r = SessionService.Instance.StopAndSave();
        if (r != null)
            MessageBox.Show($"已保存：{r.Task}\n时长 {TimeSpan.FromSeconds(r.DurationSeconds):hh\\:mm\\:ss}",
                "计时结束", MessageBoxButton.OK, MessageBoxImage.Information);
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
