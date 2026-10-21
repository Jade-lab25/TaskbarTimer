namespace TaskbarTimer.Models;

/// <summary>一条已完成的计时记录。</summary>
public class TimingRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    /// <summary>日期，格式 yyyy-MM-dd（按开始时间归属）。</summary>
    public string Date { get; set; } = "";
    /// <summary>开始时间，格式 HH:mm。</summary>
    public string Start { get; set; } = "";
    /// <summary>结束时间，格式 HH:mm。</summary>
    public string End { get; set; } = "";
    /// <summary>实际时长（秒）。</summary>
    public double DurationSeconds { get; set; }
    /// <summary>专注事项 / 任务名称。</summary>
    public string Task { get; set; } = "";
    /// <summary>进展记录（可后补）。</summary>
    public string Notes { get; set; } = "";
    /// <summary>计时模式：up=正计时，down=倒计时。</summary>
    public string Mode { get; set; } = "up";
    /// <summary>倒计时计划时长（秒），正计时为 0。</summary>
    public double PlannedSeconds { get; set; }
}
