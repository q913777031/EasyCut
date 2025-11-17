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
    /// 基于 FFmpeg 的视频处理服务实现。
    /// 依赖本机安装 ffmpeg / ffprobe，并已配置到 PATH 中。
    /// </summary>
    public sealed class FfmpegVideoProcessingService : IVideoProcessingService
    {
        /// <summary>
        /// ffmpeg 可执行文件名称（也可以改为绝对路径）
        /// </summary>
        private const string FfmpegExe = "ffmpeg";

        /// <summary>
        /// ffprobe 可执行文件名称（也可以改为绝对路径）
        /// </summary>
        private const string FfprobeExe = "ffprobe";

        /// <inheritdoc />
        public async Task<double> GetVideoDurationAsync(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                throw new ArgumentException("视频路径不能为空", nameof(videoPath));
            }

            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("找不到视频文件", videoPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = FfprobeExe,
                Arguments = $"-v error -show_entries format=duration -of csv=p=0 \"{videoPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 ffprobe 进程。");
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            string output = outputTask.Result.Trim();
            string error = errorTask.Result;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffprobe 调用失败，退出码：{process.ExitCode}{Environment.NewLine}" +
                    $"错误输出：{error}");
            }

            if (!double.TryParse(output, NumberStyles.Any, CultureInfo.InvariantCulture, out double seconds))
            {
                throw new InvalidOperationException($"无法解析视频时长：{output}");
            }

            return seconds;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> SplitVideoAsync(
            string videoPath,
            IReadOnlyList<SegmentConfig> segments,
            string outputDirectory)
        {
            if (segments is null || segments.Count == 0)
            {
                throw new ArgumentException("分段配置不能为空。", nameof(segments));
            }

            Directory.CreateDirectory(outputDirectory);
            var result = new List<string>();

            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                string segmentFileName = $"{Path.GetFileNameWithoutExtension(videoPath)}_Part{segment.Index}.mp4";
                string segmentPath = Path.Combine(outputDirectory, segmentFileName);

                string start = ToTimeString(segment.StartSeconds);
                double durationSeconds = Math.Max(segment.EndSeconds - segment.StartSeconds, 0.1);
                string duration = ToTimeString(durationSeconds);

                // -ss 起始时间，-t 时长，-c copy 表示不重新编码（更快）
                string args = $"-y -ss {start} -i \"{videoPath}\" -t {duration} -c copy \"{segmentPath}\"";

                await RunFfmpegAsync(args, outputDirectory).ConfigureAwait(false);

                result.Add(segmentPath);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<string> MergeSegmentsAsync(
            IReadOnlyList<string> segmentVideoPaths,
            string outputDirectory,
            string outputFileName)
        {
            if (segmentVideoPaths is null || segmentVideoPaths.Count == 0)
            {
                throw new ArgumentException("待合并片段列表不能为空。", nameof(segmentVideoPaths));
            }

            Directory.CreateDirectory(outputDirectory);

            string listFilePath = Path.Combine(outputDirectory, "concat_list.txt");
            var sb = new StringBuilder();

            foreach (var path in segmentVideoPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(path);
                string normalizedPath = fullPath.Replace("\\", "/").Replace("'", "''");
                sb.AppendLine($"file '{normalizedPath}'");
            }

            // 使用 UTF-8 无 BOM，避免 ffmpeg concat 解析到 BOM 报错
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            await File.WriteAllTextAsync(listFilePath, sb.ToString(), utf8NoBom).ConfigureAwait(false);

            string outputPath = Path.Combine(outputDirectory, outputFileName);

            string args = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outputPath}\"";

            await RunFfmpegAsync(args, outputDirectory).ConfigureAwait(false);

            return outputPath;
        }

        /// <inheritdoc />
        public async Task<string> ExtractAudioAsync(
            string videoPath,
            string outputDirectory,
            string baseFileName)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
            {
                throw new ArgumentException("视频路径不能为空", nameof(videoPath));
            }

            if (!File.Exists(videoPath))
            {
                throw new FileNotFoundException("找不到视频文件", videoPath);
            }

            Directory.CreateDirectory(outputDirectory);

            string safeBaseName = string.IsNullOrWhiteSpace(baseFileName)
                ? Path.GetFileNameWithoutExtension(videoPath)
                : baseFileName;

            string audioPath = Path.Combine(outputDirectory, $"{safeBaseName}_16k_mono.wav");

            // 抽取音频为 16k 单声道 PCM wav（Whisper 推荐输入）
            string args =
                $"-y -i \"{videoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{audioPath}\"";

            await RunFfmpegAsync(args, outputDirectory).ConfigureAwait(false);

            return audioPath;
        }

        /// <inheritdoc />
        public async Task<string> BurnSubtitleAsync(
            string inputVideoPath,
            string subtitlePath,
            string outputVideoPath)
        {
            if (string.IsNullOrWhiteSpace(inputVideoPath))
            {
                throw new ArgumentException("输入视频路径不能为空", nameof(inputVideoPath));
            }

            if (string.IsNullOrWhiteSpace(subtitlePath))
            {
                throw new ArgumentException("字幕文件路径不能为空", nameof(subtitlePath));
            }

            if (string.IsNullOrWhiteSpace(outputVideoPath))
            {
                throw new ArgumentException("输出视频路径不能为空", nameof(outputVideoPath));
            }

            string workDir = Path.GetDirectoryName(outputVideoPath)
                             ?? throw new InvalidOperationException("输出路径无效。");

            Directory.CreateDirectory(workDir);

            string inputFileName = Path.GetFileName(inputVideoPath);
            string subtitleFileName = Path.GetFileName(subtitlePath);
            string outputFileName = Path.GetFileName(outputVideoPath);

            // 为避免复杂转义，将工作目录设为输出目录，命令行只使用文件名。
            string args =
                $"-y -i \"{inputFileName}\" " +
                $"-vf subtitles=\"{subtitleFileName}\" " +
                "-c:v libx264 -preset veryfast -crf 20 -c:a copy " +
                $"\"{outputFileName}\"";

            await RunFfmpegAsync(args, workDir).ConfigureAwait(false);

            return outputVideoPath;
        }

        /// <summary>
        /// 内部封装 ffmpeg 调用，只重定向错误输出。
        /// </summary>
        private static async Task RunFfmpegAsync(string arguments, string? workingDirectory = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = FfmpegExe,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
            {
                psi.WorkingDirectory = workingDirectory;
            }

            using var process = new Process { StartInfo = psi };

            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动 ffmpeg 进程。");
            }

            string error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffmpeg 调用失败，退出码：{process.ExitCode}{Environment.NewLine}" +
                    $"命令行参数：{arguments}{Environment.NewLine}" +
                    $"错误输出：{error}");
            }
        }

        /// <summary>
        /// 将秒数格式化为 ffmpeg 支持的时间格式（HH:mm:ss.fff）。
        /// </summary>
        private static string ToTimeString(double seconds)
        {
            if (seconds < 0)
            {
                seconds = 0;
            }

            var ts = TimeSpan.FromSeconds(seconds);

            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }
    }
}