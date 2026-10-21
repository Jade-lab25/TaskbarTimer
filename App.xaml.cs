using System.Reflection;
using System.Threading;
using System.Windows;
using TaskbarTimer.Services;

namespace TaskbarTimer;

public partial class App : Application
{
    private static Mutex? _mutex;
    private static EventWaitHandle? _activateEvent;
    private static System.Windows.Forms.NotifyIcon? _trayIcon;

    private const string ActivateEventName = "TaskbarTimer_Activate_MainWindow";

    /// <summary>计时小窗全局引用（保持存活，避免被 GC 回收）。</summary>
    public static TimerWidget? Widget { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // 单实例保护：已有实例在运行时，唤醒它的主窗口而不是弹提示
        _mutex = new Mutex(true, "TaskbarTimer_SingleInstance_" + Assembly.GetExecutingAssembly().GetName().Name, out bool createdNew);
        if (!createdNew)
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var evt))
            {
                evt.Set(); // 通知已运行实例弹出主窗口
                evt.Dispose();
            }
            else
            {
                MessageBox.Show("计时器已在运行中，请查看系统托盘（右下角时钟旁的时钟图标）。", "任务栏计时器",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // 计时小窗创建后保持隐藏，点击「开始计时」时由 StateChanged 自动显示；
        // 这里不能 Show()，否则启动会闪出一个黑框。
        Widget = new TimerWidget();
        var main = new MainWindow();
        MainWindow = main; // 显式指定主窗口（默认会指向先实例化的小窗）
        main.Show();

        InitTrayIcon();
        InitActivateWatcher();
    }

    // ---------- 系统托盘 ----------

    private void InitTrayIcon()
    {
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream;
        var icon = new System.Drawing.Icon(iconStream);

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开主窗口", null, (_, _) => AppActions.ShowMain());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => ExitFromTray());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "任务栏计时器",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => AppActions.ShowMain();
    }

    private void ExitFromTray()
    {
        if (!SessionService.Instance.IsIdle)
        {
            var choice = MessageBox.Show("当前有计时进行中，退出将不保存本次记录。确定退出？",
                "退出确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice != MessageBoxResult.Yes) return;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
        Shutdown();
    }

    /// <summary>监听二次启动信号：弹出已隐藏/最小化的主窗口。</summary>
    private void InitActivateWatcher()
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var dispatcher = Dispatcher;
        Task.Run(async () =>
        {
            while (await Task.Run(() => _activateEvent!.WaitOne(Timeout.Infinite)))
            {
                dispatcher.Invoke(AppActions.ShowMain);
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
