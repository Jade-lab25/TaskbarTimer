using System.Windows.Threading;
using TaskbarTimer.Models;
using TaskbarTimer.Services;

namespace TaskbarTimer.Services;

public enum TimerMode { CountUp, CountDown }
public enum SessionState { Idle, Running, Paused }

/// <summary>
/// 全局计时会话（单例）。主窗口发起，任务栏小窗展示，结束后落库。
/// </summary>
public class SessionService
{
    public static SessionService Instance { get; } = new();

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();
    private DateTime _lastTick;
    private double _accumulatedSeconds; // 暂停前的累计秒数
    private AppSettings _settings = new();

    public TimerMode Mode { get; private set; } = TimerMode.CountUp;
    public SessionState State { get; private set; } = SessionState.Idle;
    public string TaskName { get; private set; } = "";
    /// <summary>进行中的进展/拆解备忘，结束后写入记录。</summary>
    public string Notes { get; private set; } = "";
    public double PlannedSeconds { get; private set; } // 倒计时计划时长
    /// <summary>正计时目标时长（秒），0 表示未设目标。</summary>
    public double TargetSeconds { get; private set; }
    /// <summary>当前会话累计分心次数。</summary>
    public int DistractionCount { get; private set; }
    public DateTime StartedAt { get; private set; }

    private double _nextCheckInSeconds; // 下次巡检时刻（相对累计秒数），0=无待发巡检
    private bool _checkInPending;       // 巡检已弹出、等待回答/超时
    private bool _goalNotified;         // 正计时目标达成是否已提示

    public event Action? Tick;
    public event Action? StateChanged;
    public event Action? Finished;           // 倒计时归零
    public event Action? GoalReached;        // 正计时目标达成
    public event Action? CheckInDue;         // 注意力巡检到点
    public event Action? DistractionChanged; // 分心计数变化

    private SessionService()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
    }

    public double ElapsedSeconds => _accumulatedSeconds;
    public double RemainingSeconds => Mode == TimerMode.CountDown
        ? Math.Max(0, PlannedSeconds - _accumulatedSeconds)
        : _accumulatedSeconds;

    public bool IsIdle => State == SessionState.Idle;

    public void Start(TimerMode mode, string taskName, double plannedSeconds, double targetSeconds = 0, string notes = "")
    {
        LoadSettings();
        Mode = mode;
        TaskName = string.IsNullOrWhiteSpace(taskName) ? "未命名任务" : taskName.Trim();
        PlannedSeconds = plannedSeconds;
        TargetSeconds = targetSeconds;
        DistractionCount = 0;
        StartedAt = DateTime.Now;
        _accumulatedSeconds = 0;
        Notes = notes ?? "";
        _checkInPending = false;
        _goalNotified = false;
        ScheduleNextCheckIn();
        State = SessionState.Running;
        _lastTick = DateTime.Now;
        _timer.Start();
        RaiseState();
    }

    public void Pause()
    {
        if (State != SessionState.Running) return;
        Accumulate();
        _timer.Stop();
        State = SessionState.Paused;
        _checkInPending = false; // 暂停期间不弹巡检
        RaiseState();
    }

    public void Resume()
    {
        if (State != SessionState.Paused) return;
        _lastTick = DateTime.Now;
        _timer.Start();
        State = SessionState.Running;
        ScheduleNextCheckIn(); // 恢复后重新生成完整随机间隔
        RaiseState();
    }

    public void Toggle()
    {
        switch (State)
        {
            case SessionState.Running: Pause(); break;
            case SessionState.Paused: Resume(); break;
        }
    }

    /// <summary>结束当前会话并保存记录；返回 null 表示当前无会话。</summary>
    public TimingRecord? StopAndSave()
    {
        if (State == SessionState.Idle) return null;
        Accumulate();
        _timer.Stop();
        _checkInPending = false;
        _nextCheckInSeconds = 0;

        var record = new TimingRecord
        {
            Date = StartedAt.ToString("yyyy-MM-dd"),
            Start = StartedAt.ToString("HH:mm"),
            End = DateTime.Now.ToString("HH:mm"),
            DurationSeconds = Math.Round(_accumulatedSeconds),
            Task = TaskName,
            Mode = Mode == TimerMode.CountDown ? "down" : "up",
            PlannedSeconds = PlannedSeconds,
            TargetSeconds = TargetSeconds,
            DistractionCount = DistractionCount,
        };
        record.Notes = Notes;
        new RecordStore().Add(record);

        State = SessionState.Idle;
        _accumulatedSeconds = 0;
        DistractionCount = 0;
        RaiseState();
        return record;
    }

    /// <summary>
    /// 无缝切换到新任务：先保存当前会话，再立即开始新会话（沿用当前模式与目标时长），
    /// 两段记录首尾相接不留空档。
    /// </summary>
    public void SwitchTask(string newTaskName)
    {
        if (State == SessionState.Idle) return;
        var mode = Mode;
        var planned = PlannedSeconds;
        var target = TargetSeconds;
        StopAndSave();
        Start(mode, newTaskName, planned, target);
    }

    /// <summary>记录一次分心（右键菜单 / 主窗口按钮）。</summary>
    public void RecordDistraction()
    {
        if (State == SessionState.Idle) return;
        DistractionCount++;
        DistractionChanged?.Invoke();
    }

    /// <summary>
    /// 巡检被回答或超时后收尾。distracted=true 记一次分心；随后重新安排下一次巡检。
    /// </summary>
    public void ResolveCheckIn(bool distracted)
    {
        if (!_checkInPending) return;
        _checkInPending = false;
        if (distracted)
        {
            DistractionCount++;
            DistractionChanged?.Invoke();
        }
        ScheduleNextCheckIn();
    }

    /// <summary>更新进行中的进展/拆解备忘。</summary>
    public void SetNotes(string value) => Notes = value;

    /// <summary>主窗口修改巡检设置后调用，让下次间隔读取生效。</summary>
    public void RefreshSettings() => LoadSettings();

    private void LoadSettings() => _settings = new SettingsStore().Get();

    private double NextCheckInInterval()
    {
        int min = Math.Max(10, _settings.CheckInMinSeconds);
        int max = Math.Max(min, _settings.CheckInMaxSeconds);
        return _random.Next(min, max + 1);
    }

    private void ScheduleNextCheckIn()
        => _nextCheckInSeconds = _accumulatedSeconds + NextCheckInInterval();

    private void Accumulate()
    {
        var now = DateTime.Now;
        if (State == SessionState.Running)
            _accumulatedSeconds += (now - _lastTick).TotalSeconds;
        _lastTick = now;
    }

    private void OnTick(object? s, EventArgs e)
    {
        Accumulate();
        if (Mode == TimerMode.CountDown && RemainingSeconds <= 0 && State == SessionState.Running)
        {
            _timer.Stop();
            State = SessionState.Paused; // 归零后停住，等待用户结束
            Finished?.Invoke();
            RaiseState();
        }
        else
        {
            // 正计时目标达成：仅温和提醒一次，计时不停止
            if (Mode == TimerMode.CountUp && TargetSeconds > 0 && !_goalNotified
                && State == SessionState.Running && _accumulatedSeconds >= TargetSeconds)
            {
                _goalNotified = true;
                GoalReached?.Invoke();
            }
            // 注意力巡检到点
            if (State == SessionState.Running && _settings.CheckInEnabled && !_checkInPending
                && _nextCheckInSeconds > 0 && _accumulatedSeconds >= _nextCheckInSeconds)
            {
                _checkInPending = true;
                CheckInDue?.Invoke();
            }
        }
        Tick?.Invoke();
    }

    private void RaiseState() => StateChanged?.Invoke();
}
