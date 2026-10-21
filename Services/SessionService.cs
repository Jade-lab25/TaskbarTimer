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
    private DateTime _lastTick;
    private double _accumulatedSeconds; // 暂停前的累计秒数

    public TimerMode Mode { get; private set; } = TimerMode.CountUp;
    public SessionState State { get; private set; } = SessionState.Idle;
    public string TaskName { get; private set; } = "";
    public double PlannedSeconds { get; private set; } // 倒计时计划时长
    public DateTime StartedAt { get; private set; }

    public event Action? Tick;
    public event Action? StateChanged;
    public event Action? Finished; // 倒计时归零

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

    public void Start(TimerMode mode, string taskName, double plannedSeconds)
    {
        Mode = mode;
        TaskName = string.IsNullOrWhiteSpace(taskName) ? "未命名任务" : taskName.Trim();
        PlannedSeconds = plannedSeconds;
        StartedAt = DateTime.Now;
        _accumulatedSeconds = 0;
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
        RaiseState();
    }

    public void Resume()
    {
        if (State != SessionState.Paused) return;
        _lastTick = DateTime.Now;
        _timer.Start();
        State = SessionState.Running;
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

        var record = new TimingRecord
        {
            Date = StartedAt.ToString("yyyy-MM-dd"),
            Start = StartedAt.ToString("HH:mm"),
            End = DateTime.Now.ToString("HH:mm"),
            DurationSeconds = Math.Round(_accumulatedSeconds),
            Task = TaskName,
            Mode = Mode == TimerMode.CountDown ? "down" : "up",
            PlannedSeconds = PlannedSeconds,
        };
        new RecordStore().Add(record);

        State = SessionState.Idle;
        _accumulatedSeconds = 0;
        RaiseState();
        return record;
    }

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
        Tick?.Invoke();
    }

    private void RaiseState() => StateChanged?.Invoke();
}
