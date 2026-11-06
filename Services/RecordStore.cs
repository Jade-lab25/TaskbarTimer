using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;using TaskbarTimer.Models;

namespace TaskbarTimer.Services;

/// <summary>JSON 文件存储基类：%APPDATA%\TaskbarTimer。</summary>
public abstract class JsonStore<T>
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarTimer");

    protected static string FilePath(string fileName) => Path.Combine(DataDir, fileName);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default,
    };

    protected static T? Load(string fileName)
    {
        try
        {
            var path = FilePath(fileName);
            if (!File.Exists(path)) return default;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default; // 数据文件损坏时不崩溃，按空数据处理
        }
    }

    protected static void Save(string fileName, T data)
    {
        Directory.CreateDirectory(DataDir);
        var path = FilePath(fileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
        File.Move(tmp, path, overwrite: true); // 先写临时文件再替换，避免写一半损坏
    }
}

/// <summary>计时记录仓库。</summary>
public class RecordStore : JsonStore<List<TimingRecord>>
{
    private const string FileName = "records.json";

    public List<TimingRecord> GetAll() => Load(FileName) ?? new List<TimingRecord>();

    public void Add(TimingRecord record)
    {
        var all = GetAll();
        all.Add(record);
        Save(FileName, all);
    }

    public void Update(TimingRecord record)
    {
        var all = GetAll();
        var idx = all.FindIndex(r => r.Id == record.Id);
        if (idx >= 0) all[idx] = record;
        Save(FileName, all);
    }

    public void Remove(string id)
    {
        var all = GetAll();
        all.RemoveAll(r => r.Id == id);
        Save(FileName, all);
    }

    /// <summary>按最近一次出现时间排序，取前 N 个去重任务名（供「切换任务」快捷按钮）。</summary>
    public List<string> RecentTaskNames(int count)
    {
        return GetAll()
            .Where(r => !string.IsNullOrWhiteSpace(r.Task))
            .GroupBy(r => r.Task.Trim())
            .OrderByDescending(g => g.Max(r => r.Date + r.Start))
            .Select(g => g.Key)
            .Take(count)
            .ToList();
    }

    /// <summary>统计某日的任务次数、总秒数与分心次数。</summary>
    public (int Count, double TotalSeconds, int Distractions) DailySummary(string date)
    {
        var rs = GetAll().Where(r => r.Date == date).ToList();
        return (rs.Count, rs.Sum(r => r.DurationSeconds), rs.Sum(r => r.DistractionCount));
    }
}

/// <summary>设置仓库。</summary>
public class SettingsStore : JsonStore<AppSettings>
{
    private const string FileName = "settings.json";

    public AppSettings Get() => Load(FileName) ?? new AppSettings();

    public void Save(AppSettings settings) => Save(FileName, settings);
}
