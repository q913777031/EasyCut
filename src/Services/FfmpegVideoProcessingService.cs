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

        /// <summary>
        /// Windows 下常见的中文字体文件（可按需要修改）。
        /// </summary>
        private const string DefaultFontFile = @"C:\Windows\Fonts\msyh.ttc";

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
                throw new ArgumentException("分段配置不能为空。", nameof(segments));

            Directory.CreateDirectory(outputDirectory);

            var result = new List<string>();

            foreach (var segment in segments)
            {
                string segmentFileName = $"{Path.GetFileNameWithoutExtension(videoPath)}_Part{segment.Index}.mp4";
                string segmentPath = Path.Combine(outputDirectory, segmentFileName);

                double durationSeconds = Math.Max(segment.EndSeconds - segment.StartSeconds, 0.1);
                string start = ToTimeString(segment.StartSeconds);
                string duration = ToTimeString(durationSeconds);

                // 统一重编码 + 重置时间戳 + 音频转为 2 声道
                string args =
                    $"-y -ss {start} -i \"{videoPath}\" " +
                    $"-t {duration} " +
             "-c:v libx264 -preset veryfast -crf 20 " +
             "-c:a aac -ac 2 -ar 48000 -b:a 192k " +
             "-reset_timestamps 1 " +
                    $"\"{segmentPath}\"";

                await RunFfmpegAsync(args, outputDirectory).ConfigureAwait(false);

                result.Add(segmentPath);
            }

            return result;
        }

        /// <inheritdoc />
        /// <inheritdoc />
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

            string outputPath = Path.Combine(outputDirectory, outputFileName);

            // 1. 构造输入参数：-i "seg0" -i "seg1" ...
            var argsBuilder = new StringBuilder("-y ");

            for (int i = 0; i < segmentVideoPaths.Count; i++)
            {
                string path = segmentVideoPaths[i];
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string fullPath = Path.GetFullPath(path);
                argsBuilder.Append("-i \"").Append(fullPath).Append("\" ");
            }

            // 2. 构造 filter_complex：
            //    [0:v][0:a][1:v][1:a]...[N-1:v][N-1:a]concat=n=N:v=1:a=1[v][a]
            var filterBuilder = new StringBuilder();

            int inputCount = segmentVideoPaths.Count;

            for (int i = 0; i < inputCount; i++)
            {
                filterBuilder.Append('[').Append(i).Append(":v]");
                filterBuilder.Append('[').Append(i).Append(":a]");
            }

            filterBuilder.Append("concat=n=")
                         .Append(inputCount)
                         .Append(":v=1:a=1[v][a]");

            argsBuilder.Append("-filter_complex \"")
                       .Append(filterBuilder)
                       .Append("\" ");

            // 3. 映射过滤后的视频 / 音频，并统一编码参数
            argsBuilder.Append("-map \"[v]\" -map \"[a]\" ")
                       .Append("-c:v libx264 -preset veryfast -crf 20 ")
                       .Append("-c:a aac -ac 2 -ar 48000 -b:a 192k ")
                       .Append("\"").Append(outputPath).Append('"');

            string args = argsBuilder.ToString();

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

            string audioPath = Path.Combine(outputDirectory, $"{safeBaseName}_16k_mono_60s.wav");

            // 关键：只抽取前 60 秒音频，加快 Whisper 处理
            // -i 输入视频，-vn 去掉视频流，-ar 16000 / -ac 1 适配 Whisper，-t 60 只保留 60 秒
            string args =
                $"-y -i \"{videoPath}\" " +
                "-vn -acodec pcm_s16le -ar 16000 -ac 1 " +
                "-t 60 " +
                $"\"{audioPath}\"";

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
        /// 生成黑底白字中文提示片头。
        /// </summary>
        /// <summary>
        /// 生成黑底白字中文提示片头（带静音音轨，便于 concat）。
        /// </summary>
        public async Task<string> CreateTitleCardAsync(
            string text,
            string outputDirectory,
            string fileName,
            double durationSeconds = 2.0)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("片头文字不能为空。", nameof(text));

            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空。", nameof(outputDirectory));

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("输出文件名不能为空。", nameof(fileName));

            Directory.CreateDirectory(outputDirectory);

            string outputPath = Path.Combine(outputDirectory, fileName);

            // 处理单引号，避免 drawtext 解析错误
            string safeText = text.Replace("'", "''");

            const int width = 1280;
            const int height = 720;

            string durationStr = durationSeconds.ToString(CultureInfo.InvariantCulture);

            // 这里生成：
            //  - 输入 0：黑色画面 color（带长度 d）
            //  - 输入 1：静音音频 anullsrc（立体声 + 48k）
            //  - drawtext 叠字到视频上
            //  - 最终编码为 H.264 + AAC 立体声 48k
            // 这样就和 SplitVideoAsync 生成的片段参数一致了
            string args =
                $"-y " +
                $"-f lavfi -i color=c=black:s={width}x{height}:d={durationStr} " + // 视频
                "-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=48000 " +  // 静音音频
                "-shortest " +                                                    // 以较短流为准
                "-vf \"drawtext=font='Microsoft YaHei':" +
                $"text='{safeText}':" +
                "fontcolor=white:fontsize=48:" +
                "x=(w-text_w)/2:y=(h-text_h)/2\" " +
                "-c:v libx264 -preset veryfast -crf 20 " +
                "-c:a aac -ac 2 -ar 48000 -b:a 192k " +
                "-pix_fmt yuv420p " +
                $"-t {durationStr} " + // 再次限定时长（双保险）
                $"\"{outputPath}\"";

            await RunFfmpegAsync(args, outputDirectory).ConfigureAwait(false);

            return outputPath;
        }

        /// <summary>
        /// 运行 FFmpeg 并处理错误（你之前已经有类似实现，可重用）。
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