using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// 封装 ffmpeg 调用的服务类。
    /// </summary>
    public class FfmpegService
    {
        /// <summary>
        /// 获取或设置 ffmpeg 可执行文件路径。
        /// 默认指向程序目录下的 ffmpeg.exe。
        /// </summary>
        public string FfmpegPath { get; set; }

        /// <summary>
        /// 初始化 ffmpeg 服务实例。
        /// </summary>
        public FfmpegService()
        {
            FfmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        }

        /// <summary>
        /// 生成无字幕 + 英文字幕 + 中英字幕 + 无字幕的学习视频。
        /// </summary>
        /// <param name="videoPath">原始视频路径。</param>
        /// <param name="englishSubtitlePath">英文字幕路径。</param>
        /// <param name="bilingualSubtitlePath">中英字幕路径。</param>
        /// <param name="start">片段开始时间。</param>
        /// <param name="end">片段结束时间。</param>
        /// <param name="outputPath">最终输出视频路径。</param>
        /// <param name="logAction">日志输出回调。</param>
        /// <returns>表示异步操作的任务。</returns>
        public async Task GenerateLearningVideoAsync(
            string videoPath,
            string englishSubtitlePath,
            string bilingualSubtitlePath,
            TimeSpan start,
            TimeSpan end,
            string outputPath,
            Action<string> logAction)
        {
            if (logAction == null)
            {
                throw new ArgumentNullException("logAction");
            }

            if (!File.Exists(FfmpegPath))
            {
                throw new FileNotFoundException("未找到 ffmpeg.exe，请将 ffmpeg.exe 放在程序目录。", FfmpegPath);
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "EasyCut");
            Directory.CreateDirectory(tempRoot);

            string workDir = Path.Combine(tempRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss_ffff"));
            Directory.CreateDirectory(workDir);

            logAction("临时工作目录：" + workDir);

            string seg1 = Path.Combine(workDir, "seg1_raw.mp4");
            string seg2 = Path.Combine(workDir, "seg2_eng.mp4");
            string seg3 = Path.Combine(workDir, "seg3_bi.mp4");
            string seg4 = Path.Combine(workDir, "seg4_raw.mp4");
            string listFile = Path.Combine(workDir, "list.txt");

            string startText = ToFfmpegTime(start);
            string endText = ToFfmpegTime(end);

            // 第一段：原始片段，无字幕。
            await RunFfmpegAsync(
                "-ss " + startText + " -to " + endText + " -i \"" + videoPath + "\" -c copy \"" + seg1 + "\"",
                workDir,
                logAction);

            // 第二段：英文字幕。
            await RunFfmpegAsync(
                "-ss " + startText + " -to " + endText + " -i \"" + videoPath + "\" -vf \"subtitles='" + NormalizePath(englishSubtitlePath) + "'\" -c:a copy \"" + seg2 + "\"",
                workDir,
                logAction);

            // 第三段：中英字幕。
            await RunFfmpegAsync(
                "-ss " + startText + " -to " + endText + " -i \"" + videoPath + "\" -vf \"subtitles='" + NormalizePath(bilingualSubtitlePath) + "'\" -c:a copy \"" + seg3 + "\"",
                workDir,
                logAction);

            // 第四段：再次原始片段。
            await RunFfmpegAsync(
                "-ss " + startText + " -to " + endText + " -i \"" + videoPath + "\" -c copy \"" + seg4 + "\"",
                workDir,
                logAction);

            // 生成 concat 列表文件。
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("file '" + NormalizePath(seg1) + "'");
            builder.AppendLine("file '" + NormalizePath(seg2) + "'");
            builder.AppendLine("file '" + NormalizePath(seg3) + "'");
            builder.AppendLine("file '" + NormalizePath(seg4) + "'");
            await File.WriteAllTextAsync(listFile, builder.ToString(), Encoding.UTF8);

            // 确保输出路径目录存在。
            string outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // 拼接为一个输出文件。
            await RunFfmpegAsync(
                "-f concat -safe 0 -i \"" + listFile + "\" -c copy \"" + outputPath + "\"",
                workDir,
                logAction);

            logAction("输出文件：" + outputPath);
        }

        /// <summary>
        /// 将时间转换为 ffmpeg 使用的时间格式。
        /// </summary>
        /// <param name="time">时间值。</param>
        /// <returns>格式化后的时间字符串。</returns>
        private static string ToFfmpegTime(TimeSpan time)
        {
            return string.Format("{0:00}:{1:00}:{2:00}", (int)time.TotalHours, time.Minutes, time.Seconds);
        }

        /// <summary>
        /// 将路径转换为 ffmpeg 友好的格式（使用 / 分隔符）。
        /// </summary>
        /// <param name="path">原始路径。</param>
        /// <returns>转换后的路径字符串。</returns>
        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            return path.Replace("\\", "/");
        }

        /// <summary>
        /// 执行一条 ffmpeg 命令。
        /// </summary>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="workingDirectory">工作目录。</param>
        /// <param name="logAction">日志输出回调。</param>
        /// <returns>表示异步操作的任务。</returns>
        private async Task RunFfmpegAsync(string arguments, string workingDirectory, Action<string> logAction)
        {
            logAction("执行 ffmpeg：ffmpeg " + arguments);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = FfmpegPath;
            startInfo.Arguments = arguments;
            startInfo.WorkingDirectory = workingDirectory;
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.CreateNoWindow = true;

            using (Process process = new Process())
            {
                process.StartInfo = startInfo;

                StringBuilder outputBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (outputBuilder)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (outputBuilder)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await Task.Run(() => process.WaitForExit());

                string output = outputBuilder.ToString();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    logAction(output);
                }

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("ffmpeg 执行失败，退出代码：" + process.ExitCode);
                }
            }
        }
    }
}