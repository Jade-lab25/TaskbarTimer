namespace TaskbarTimer.Models;

/// <summary>应用设置（窗口位置等），持久化到 %APPDATA%\TaskbarTimer。</summary>
public class AppSettings
{
    public double WidgetX { get; set; } = double.NaN;
    public double WidgetY { get; set; } = double.NaN;
    /// <summary>倒计时结束时是否播放提示音。</summary>
    public bool PlaySoundOnFinish { get; set; } = true;
}
