using System.IO;
using System.Text;
using TaskbarTimer.Models;

namespace TaskbarTimer.Services;

/// <summary>导出计时记录为 CSV（UTF-8 带 BOM，WPS 可直接打开不乱码）。</summary>
public static class CsvExporter
{
    public static string Export(IEnumerable<TimingRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("日期,开始时间,结束时间,时长(分钟),专注事项,进展记录,模式");
        foreach (var r in records)
        {
            sb.AppendLine(string.Join(",",
                Esc(r.Date),
                Esc(r.Start),
                Esc(r.End),
                (r.DurationSeconds / 60.0).ToString("F1"),
                Esc(r.Task),
                Esc(r.Notes),
                r.Mode == "down" ? "倒计时" : "正计时"));
        }
        return sb.ToString();
    }

    /// <summary>写入文件并返回完整路径。</summary>
    public static string WriteFile(IEnumerable<TimingRecord> records, string directory)
    {
        var fileName = $"计时记录_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, Export(records), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    private static string Esc(string? s)
    {
        s ??= "";
        // 含逗号/引号/换行时按 CSV 规范加引号转义
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
