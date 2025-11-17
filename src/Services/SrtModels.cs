#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// SRT 字幕条目
    /// </summary>
    public sealed class SrtEntry
    {
        /// <summary>
        /// 序号（从 1 开始）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 起始时间
        /// </summary>
        public TimeSpan Start { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public TimeSpan End { get; set; }

        /// <summary>
        /// 字幕文本行集合
        /// </summary>
        public List<string> Lines { get; } = new List<string>();
    }

    /// <summary>
    /// SRT 字幕读写辅助类
    /// </summary>
    public static class SrtHelper
    {
        /// <summary>
        /// 异步读取 SRT 文件为条目列表。
        /// </summary>
        public static async Task<IReadOnlyList<SrtEntry>> ReadAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("路径不能为空。", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException("找不到 SRT 文件。", path);
            }

            var result = new List<SrtEntry>();

            using var reader = new StreamReader(path, Encoding.UTF8);

            while (true)
            {
                string? indexLine = await reader.ReadLineAsync();
                if (indexLine is null)
                {
                    break;
                }

                indexLine = indexLine.Trim();
                if (indexLine.Length == 0)
                {
                    continue;
                }

                if (!int.TryParse(indexLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    // 非标准格式，跳过
                    continue;
                }

                string? timeLine = await reader.ReadLineAsync();
                if (timeLine is null)
                {
                    break;
                }

                if (!TryParseTimeLine(timeLine, out var start, out var end))
                {
                    // 时间行解析失败，跳过该条
                    continue;
                }

                var entry = new SrtEntry
                {
                    Index = index,
                    Start = start,
                    End = end
                };

                while (true)
                {
                    string? textLine = await reader.ReadLineAsync();
                    if (textLine is null || textLine.Length == 0)
                    {
                        break;
                    }

                    entry.Lines.Add(textLine);
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// 异步写入 SRT 文件。
        /// </summary>
        public static async Task WriteAsync(string path, IReadOnlyList<SrtEntry> entries)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("路径不能为空。", nameof(path));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var sb = new StringBuilder();

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                int index = e.Index > 0 ? e.Index : i + 1;

                sb.AppendLine(index.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine($"{FormatTime(e.Start)} --> {FormatTime(e.End)}");

                foreach (var line in e.Lines)
                {
                    sb.AppendLine(line);
                }

                sb.AppendLine();
            }

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// 解析时间段行，例如 "00:00:01,000 --> 00:00:03,000"。
        /// </summary>
        private static bool TryParseTimeLine(string line, out TimeSpan start, out TimeSpan end)
        {
            start = default;
            end = default;

            var parts = line.Split(new[] { " --> " }, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                return false;
            }

            if (!TryParseTime(parts[0].Trim(), out start))
            {
                return false;
            }

            if (!TryParseTime(parts[1].Trim(), out end))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 解析 "00:00:01,000" 这种时间。
        /// </summary>
        private static bool TryParseTime(string text, out TimeSpan time)
        {
            text = text.Replace('.', ',');
            return TimeSpan.TryParseExact(
                text,
                @"hh\:mm\:ss\,fff",
                CultureInfo.InvariantCulture,
                out time);
        }

        /// <summary>
        /// 格式化 TimeSpan 为 SRT 时间字符串。
        /// </summary>
        private static string FormatTime(TimeSpan time)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00},{3:000}",
                (int)time.TotalHours,
                time.Minutes,
                time.Seconds,
                time.Milliseconds);
        }
    }
}