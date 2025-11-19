#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// SRT 字幕解析、写入、裁剪与自动选片段帮助类。
    /// </summary>
    public static class SrtHelper
    {
        /// <summary>
        /// 解析 SRT 文件为字幕条目列表。
        /// </summary>
        /// <param name="path">SRT 文件路径。</param>
        /// <returns>字幕条目列表。</returns>
        public static IReadOnlyList<SrtEntry> Parse(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("找不到 SRT 文件。", path);
            }

            var lines = File.ReadAllLines(path);
            var result = new List<SrtEntry>();
            var index = 0;

            while (index < lines.Length)
            {
                // 跳过空行
                while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                {
                    index++;
                }

                if (index >= lines.Length)
                {
                    break;
                }

                // 索引行（可忽略解析失败）
                var indexLine = lines[index].Trim();
                _ = int.TryParse(indexLine, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
                index++;

                if (index >= lines.Length)
                {
                    break;
                }

                // 时间行：00:00:01,000 --> 00:00:03,500
                var timeLine = lines[index].Trim();
                index++;

                var arrowIndex = timeLine.IndexOf("-->", StringComparison.Ordinal);
                if (arrowIndex <= 0)
                {
                    continue;
                }

                var startText = timeLine[..arrowIndex].Trim();
                var endText = timeLine[(arrowIndex + 3)..].Trim();

                var start = ParseSrtTime(startText);
                var end = ParseSrtTime(endText);

                // 文本行：直到遇到空行
                var entry = new SrtEntry
                {
                    Index = number,
                    Start = start,
                    End = end
                };

                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    entry.Lines.Add(lines[index]);
                    index++;
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// 同步写入 SRT 文件（内部调用异步版本）。
        /// </summary>
        /// <param name="path">输出文件路径。</param>
        /// <param name="entries">字幕条目集合。</param>
        public static void Write(string path, IEnumerable<SrtEntry> entries)
        {
            var list = entries as IReadOnlyList<SrtEntry> ?? entries.ToList();
            WriteAsync(path, list, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// 异步写入 SRT 文件。
        /// </summary>
        /// <param name="path">输出文件路径。</param>
        /// <param name="entries">字幕条目集合。</param>
        /// <param name="cancellationToken">取消标记。</param>
        public static async Task WriteAsync(
            string path,
            IReadOnlyList<SrtEntry> entries,
            CancellationToken cancellationToken = default)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            await using var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);

            await using var writer = new StreamWriter(stream, encoding);

            for (var i = 0; i < entries.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = entries[i];

                var idxString = (i + 1).ToString(CultureInfo.InvariantCulture);
                var start = FormatSrtTime(entry.Start);
                var end = FormatSrtTime(entry.End);

                await writer.WriteLineAsync(idxString).ConfigureAwait(false);
                await writer.WriteLineAsync($"{start} --> {end}").ConfigureAwait(false);

                if (entry.Lines.Count == 0 && !string.IsNullOrWhiteSpace(entry.Text))
                {
                    await writer.WriteLineAsync(entry.Text).ConfigureAwait(false);
                }
                else
                {
                    foreach (var line in entry.Lines)
                    {
                        await writer.WriteLineAsync(line).ConfigureAwait(false);
                    }
                }

                await writer.WriteLineAsync().ConfigureAwait(false);
            }

            await writer.FlushAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 裁剪并平移 SRT 字幕：保留指定时间范围内的内容，并将起点平移到 0 秒。
        /// </summary>
        /// <param name="sourceSrtPath">源 SRT 路径。</param>
        /// <param name="clipStartSeconds">裁剪起点（秒）。</param>
        /// <param name="clipEndSeconds">裁剪终点（秒）。</param>
        /// <param name="outputDirectory">输出目录。</param>
        /// <param name="outputFileName">输出文件名（含 .srt）。</param>
        /// <returns>新生成的 SRT 路径。</returns>
        public static string CropAndShift(
            string sourceSrtPath,
            double clipStartSeconds,
            double clipEndSeconds,
            string outputDirectory,
            string outputFileName)
        {
            var all = Parse(sourceSrtPath);

            var clipStart = TimeSpan.FromSeconds(Math.Max(0, clipStartSeconds));
            var clipEnd = TimeSpan.FromSeconds(Math.Max(clipStartSeconds, clipEndSeconds));

            var result = new List<SrtEntry>();
            var newIndex = 1;

            foreach (var entry in all)
            {
                if (entry.End <= clipStart || entry.Start >= clipEnd)
                {
                    continue;
                }

                var start = entry.Start < clipStart ? clipStart : entry.Start;
                var end = entry.End > clipEnd ? clipEnd : entry.End;

                if (end <= start)
                {
                    continue;
                }

                var shifted = new SrtEntry
                {
                    Index = newIndex++,
                    Start = start - clipStart,
                    End = end - clipStart
                };

                shifted.Lines.AddRange(entry.Lines);

                result.Add(shifted);
            }

            var fullPath = Path.Combine(outputDirectory, outputFileName);
            Write(fullPath, result);
            return fullPath;
        }

        /// <summary>
        /// 根据字幕内容自动选择一段适合学习的片段时间范围。
        /// </summary>
        /// <param name="subtitles">字幕列表。</param>
        /// <param name="totalDurationSeconds">视频总时长（秒）。</param>
        /// <param name="minDuration">片段最短时长（秒）。</param>
        /// <param name="maxDuration">片段最长时长（秒）。</param>
        /// <param name="targetDuration">期望片段时长（秒）。</param>
        /// <returns>起止时间（秒）。</returns>
        /// <summary>
        /// 根据字幕内容自动选择一段适合学习的片段时间范围（启发式规则版）。
        /// - 综合考虑：时长、单词数、语速、是否句子完整、在视频中的位置。
        /// - 不再死卡 60 秒，可通过参数控制时长倾向。
        /// </summary>
        public static (double StartSeconds, double EndSeconds) PickBestSegment(
            IReadOnlyList<SrtEntry> subtitles,
            double totalDurationSeconds,
            double minDuration = 4.0,
            double maxDuration = 40.0,
            double targetDuration = 15.0)
        {
            totalDurationSeconds = Math.Max(totalDurationSeconds, 1.0);

            if (subtitles.Count == 0)
            {
                // 没字幕：兜底用前 targetDuration 秒
                var end = Math.Min(targetDuration > 0 ? targetDuration : totalDurationSeconds,
                    totalDurationSeconds);
                return (0, end);
            }

            // 参数兜底，防止传入 0 / 负数
            if (minDuration <= 0)
            {
                minDuration = 2.0;
            }

            if (maxDuration <= 0 || maxDuration > totalDurationSeconds)
            {
                maxDuration = totalDurationSeconds;
            }

            if (targetDuration <= 0)
            {
                targetDuration = Math.Min(15.0, maxDuration);
            }

            double bestScore = double.NegativeInfinity;
            double bestStartSec = 0;
            double bestEndSec = Math.Min(targetDuration, totalDurationSeconds);
            var found = false;

            // 遍历所有起点 i，从 i 开始向后扩展字幕组成候选片段。
            for (var i = 0; i < subtitles.Count; i++)
            {
                var start = subtitles[i].Start;
                var end = subtitles[i].End;
                var lines = new List<string>(subtitles[i].Lines);

                for (var j = i; j < subtitles.Count; j++)
                {
                    if (j > i)
                    {
                        var s = subtitles[j];
                        end = s.End;
                        lines.AddRange(s.Lines);
                    }

                    var duration = (end - start).TotalSeconds;

                    // 太短直接跳过，太长就停止对这个起点继续扩展
                    if (duration < minDuration)
                    {
                        continue;
                    }

                    if (duration > maxDuration)
                    {
                        break;
                    }

                    var text = string.Join(" ", lines.Select(l => l.Trim()));
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    var wordCount = CountWords(text);
                    // 片段太短就没学习意义
                    if (wordCount < 5)
                    {
                        continue;
                    }

                    // ===== 启发式打分 =====

                    // 语速：单词数 / 秒，正常英语大约 2 ~ 3 词/秒
                    var speechRate = wordCount / Math.Max(duration, 0.1);
                    const double idealSpeechRate = 2.2; // 理想语速
                    var speechRateScore = -Math.Abs(speechRate - idealSpeechRate); // 越接近越好（0，偏离越大越负）

                    // 是否以句号/问号/感叹号结束，完整句子加分
                    var trimmed = text.TrimEnd();
                    var endsWithPunc = trimmed.EndsWith(".", StringComparison.Ordinal) ||
                                       trimmed.EndsWith("?", StringComparison.Ordinal) ||
                                       trimmed.EndsWith("!", StringComparison.Ordinal);
                    var punctuationScore = endsWithPunc ? 1.0 : 0.0;

                    // 时长是否接近 targetDuration（目标只是“理想”，不会硬卡）
                    var lengthScore = -Math.Abs(duration - targetDuration) / targetDuration; // [-1, 0] 左右

                    // 在视频中的位置：稍微偏中间一点最好，避免片头片尾闲聊
                    var mid = (start + end).TotalSeconds / 2.0;
                    var pos = mid / totalDurationSeconds; // [0,1]
                    var centerScore = -Math.Abs(pos - 0.5); // 越接近 0.5 越接近 0，越偏两头越负

                    // 总分：可以按需要调整权重
                    var score =
                        2.0 * punctuationScore +       // 强调“完整句子”
                        1.5 * speechRateScore +        // 语速适中
                        1.0 * lengthScore +            // 时长接近目标
                        0.3 * centerScore;             // 略偏中间

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestStartSec = start.TotalSeconds;
                        bestEndSec = end.TotalSeconds;
                        found = true;
                    }
                }
            }

            if (!found)
            {
                // 兜底：用所有字幕的范围
                var first = subtitles[0];
                var last = subtitles[^1];
                bestStartSec = first.Start.TotalSeconds;
                bestEndSec = last.End.TotalSeconds;
            }

            // 安全裁到视频范围内
            bestStartSec = Math.Max(0, bestStartSec);
            bestEndSec = Math.Min(totalDurationSeconds, bestEndSec);

            if (bestEndSec <= bestStartSec)
            {
                bestEndSec = Math.Min(
                    totalDurationSeconds,
                    bestStartSec + Math.Min(targetDuration, maxDuration));
            }

            return (bestStartSec, bestEndSec);
        }

        /// <summary>
        /// 粗略统计英文单词数量。
        /// </summary>
        /// <param name="text">字幕文本。</param>
        /// <returns>单词数。</returns>
        public static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var parts = text.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            return parts.Length;
        }

        /// <summary>
        /// 解析 SRT 时间字符串。
        /// </summary>
        private static TimeSpan ParseSrtTime(string text)
        {
            return TimeSpan.ParseExact(
                text.Trim(),
                @"hh\:mm\:ss\,fff",
                CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将时间格式化为 SRT 时间字符串。
        /// </summary>
        private static string FormatSrtTime(TimeSpan time)
        {
            return time.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// SRT 字幕条目。
    /// </summary>
    public sealed class SrtEntry
    {
        /// <summary>
        /// 索引编号。
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 开始时间。
        /// </summary>
        public TimeSpan Start { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        public TimeSpan End { get; set; }

        /// <summary>
        /// 文本行集合。
        /// </summary>
        public List<string> Lines { get; } = new();

        /// <summary>
        /// 合并后的完整文本（按行拼接）。
        /// </summary>
        public string Text => string.Join(Environment.NewLine, Lines);
    }
}