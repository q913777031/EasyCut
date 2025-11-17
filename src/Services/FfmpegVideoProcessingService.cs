#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 基于 FFmpeg 的视频处理服务实现
    /// </summary>
    public class FfmpegVideoProcessingService : IVideoProcessingService
    {
        /// <summary>
        /// FFmpeg 命令名称（可改为绝对路径）
        /// </summary>
        private const string FfmpegExe = "ffmpeg";

        /// <summary>
        /// FFprobe 命令名称（可改为绝对路径）
        /// </summary>
        private const string FfprobeExe = "ffprobe";

        /// <summary>
        /// 获取视频总时长（秒）
        /// </summary>
        public async Task<double> GetVideoDurationAsync(string videoPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfprobeExe,
                Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true, // 同时重定向两个流
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 ffprobe 进程");
            }

            // 同时读 stdout + stderr，避免其中一个缓冲区写满造成子进程阻塞
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            string output = outputTask.Result;
            string error = errorTask.Result;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffprobe 调用失败，退出码：{process.ExitCode}{Environment.NewLine}" +
                    $"命令行参数：{psi.Arguments}{Environment.NewLine}" +
                    $"错误输出：{error}");
            }

            if (!double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
            {
                throw new InvalidOperationException($"无法解析视频时长：{output}");
            }

            return seconds;
        }

        /// <summary>
        /// 合并多个视频片段为一个输出文件（使用 FFmpeg concat）
        /// </summary>
        public async Task<string> MergeSegmentsAsync(
       IReadOnlyList<string> segmentVideoPaths,
       string outputDirectory,
       string outputFileName)
        {
            Directory.CreateDirectory(outputDirectory);

            string listFilePath = Path.Combine(outputDirectory, "concat_list.txt");
            var sb = new StringBuilder();

            foreach (var path in segmentVideoPaths)
            {
                // 1. Windows 路径改成正斜杠，更稳一点（可选）
                string normalizedPath = path.Replace("\\", "/");

                // 2. 路径里如果有单引号，按 ffmpeg 规则替换成两个单引号
                normalizedPath = normalizedPath.Replace("'", "''");

                // 3. 每行严格以 file 开头
                sb.AppendLine($"file '{normalizedPath}'");
            }

            // 4. 关键：UTF-8 无 BOM
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            await File.WriteAllTextAsync(listFilePath, sb.ToString(), utf8NoBom)
                      .ConfigureAwait(false);

            string outputPath = Path.Combine(outputDirectory, outputFileName);

            string args = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outputPath}\"";

            await RunFfmpegAsync(args).ConfigureAwait(false);

            return outputPath;
        }

        /// <summary>
        /// 根据分段配置切分视频
        /// </summary>
        public async Task<IReadOnlyList<string>> SplitVideoAsync(
            string videoPath,
            IReadOnlyList<SegmentConfig> segments,
            string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            var result = new List<string>();

            foreach (var segment in segments)
            {
                string segmentFileName = $"{Path.GetFileNameWithoutExtension(videoPath)}_Part{segment.Index}.mp4";
                string segmentPath = Path.Combine(outputDirectory, segmentFileName);

                string start = ToTimeString(segment.StartSeconds);
                double durationSeconds = Math.Max(segment.EndSeconds - segment.StartSeconds, 0.1);
                string duration = ToTimeString(durationSeconds);

                string args = $"-y -ss {start} -i \"{videoPath}\" -t {duration} -c copy \"{segmentPath}\"";

                await RunFfmpegAsync(args).ConfigureAwait(false);

                result.Add(segmentPath);
            }

            return result;
        }

        /// <summary>
        /// 运行 FFmpeg 并处理错误（只读 stderr，避免输出阻塞）
        /// </summary>
        private static async Task RunFfmpegAsync(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegExe,
                Arguments = arguments,
                RedirectStandardError = true,   // 只重定向错误输出
                RedirectStandardOutput = false, // 不需要标准输出
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 ffmpeg 进程");
            }

            string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffmpeg 调用失败：{error}");
            }
        }

        /// <summary>
        /// 将秒数格式化为 ffmpeg 支持的时间格式（HH:mm:ss.fff）
        /// </summary>
        private static string ToTimeString(double seconds)
        {
            if (seconds < 0) seconds = 0;
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }
    }
}