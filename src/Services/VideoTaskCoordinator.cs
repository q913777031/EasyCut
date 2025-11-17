#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频任务协调服务：
    /// - 抽取音频；
    /// - Whisper 生成英文 / 中英字幕；
    /// - 等分四段视频；
    /// - 第二段叠英文字幕、第三段叠中英字幕；
    /// - 合并四段生成最终成品。
    /// </summary>
    public sealed class VideoTaskCoordinator : IVideoTaskCoordinator
    {
        private readonly IVideoTaskRepository _repository;
        private readonly IVideoProcessingService _videoProcessingService;
        private readonly ISubtitleGenerator _subtitleGenerator;

        /// <summary>
        /// 构造函数
        /// </summary>
        public VideoTaskCoordinator(
            IVideoTaskRepository repository,
            IVideoProcessingService videoProcessingService,
            ISubtitleGenerator subtitleGenerator)
        {
            _repository = repository;
            _videoProcessingService = videoProcessingService;
            _subtitleGenerator = subtitleGenerator;
        }

        /// <inheritdoc />
        public async Task<VideoTask> CreateAndRunTaskAsync(string inputVideoPath, string outputDirectory)
        {
            if (string.IsNullOrWhiteSpace(inputVideoPath))
            {
                throw new ArgumentException("视频路径不能为空。", nameof(inputVideoPath));
            }

            if (!File.Exists(inputVideoPath))
            {
                throw new FileNotFoundException("找不到输入视频文件。", inputVideoPath);
            }

            Directory.CreateDirectory(outputDirectory);

            var now = DateTime.Now;
            var task = new VideoTask
            {
                Id = Guid.NewGuid(),
                InputVideoPath = inputVideoPath,
                OutputDirectory = outputDirectory,
                Name = Path.GetFileNameWithoutExtension(inputVideoPath),
                Status = VideoTaskStatus.Pending,
                Progress = 0,
                CreatedTime = now,
                UpdatedTime = now
            };

            await _repository.InsertAsync(task).ConfigureAwait(false);

            try
            {
                // 状态：处理中
                task.Status = VideoTaskStatus.Processing;
                task.Progress = 5;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 1. 抽取音频为 16k 单声道 wav
                string audioPath = await _videoProcessingService
                    .ExtractAudioAsync(inputVideoPath, outputDirectory, task.Name)
                    .ConfigureAwait(false);

                task.Progress = 15;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 2. 使用 Whisper 生成英文 + 中英字幕 SRT
                var (englishSrtPath, bilingualSrtPath) =
                    await _subtitleGenerator.GenerateEnglishAndBilingualSrtAsync(
                        audioPath,
                        outputDirectory,
                        baseFileName: task.Name,
                        translateAsync: null) // 先不翻译，后续可注入翻译函数
                        .ConfigureAwait(false);

                task.Progress = 35;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 3. 获取视频总时长并等分四段
                double duration = await _videoProcessingService
                    .GetVideoDurationAsync(inputVideoPath)
                    .ConfigureAwait(false);

                var segments = BuildFourSegments(duration);

                task.Progress = 45;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 4. 切分视频成四段
                var segmentPaths = await _videoProcessingService
                    .SplitVideoAsync(inputVideoPath, segments, outputDirectory)
                    .ConfigureAwait(false);

                task.Progress = 65;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 5. 读取完整字幕（英文 & 中英）
                var allEnglishSubs = await SrtHelper.ReadAsync(englishSrtPath).ConfigureAwait(false);
                var allBilingualSubs = await SrtHelper.ReadAsync(bilingualSrtPath).ConfigureAwait(false);

                task.Progress = 75;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 6. 为第二 / 三段生成对应的分段字幕并烧入
                var finalSegmentPaths = new List<string>();

                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    string originalSegPath = segmentPaths[i];

                    // 第一段：无字幕
                    if (seg.Index == 1)
                    {
                        finalSegmentPaths.Add(originalSegPath);
                        continue;
                    }

                    // 第四段：无字幕
                    if (seg.Index == 4)
                    {
                        finalSegmentPaths.Add(originalSegPath);
                        continue;
                    }

                    // 计算当前段的字幕时间范围
                    double segStart = seg.StartSeconds;
                    double segEnd = seg.EndSeconds;

                    if (seg.Index == 2)
                    {
                        // 第二段：英文字幕
                        var segEntries = SliceAndShift(allEnglishSubs, segStart, segEnd);
                        string segSrtPath = Path.Combine(
                            outputDirectory,
                            $"{task.Name}_Part{seg.Index}.en.srt");

                        await SrtHelper.WriteAsync(segSrtPath, segEntries).ConfigureAwait(false);

                        string segOutPath = Path.Combine(
                            outputDirectory,
                            $"{task.Name}_Part{seg.Index}_en.mp4");

                        string burnedPath = await _videoProcessingService
                            .BurnSubtitleAsync(originalSegPath, segSrtPath, segOutPath)
                            .ConfigureAwait(false);

                        finalSegmentPaths.Add(burnedPath);
                        continue;
                    }

                    if (seg.Index == 3)
                    {
                        // 第三段：中英字幕
                        var segEntries = SliceAndShift(allBilingualSubs, segStart, segEnd);
                        string segSrtPath = Path.Combine(
                            outputDirectory,
                            $"{task.Name}_Part{seg.Index}.en-zh.srt");

                        await SrtHelper.WriteAsync(segSrtPath, segEntries).ConfigureAwait(false);

                        string segOutPath = Path.Combine(
                            outputDirectory,
                            $"{task.Name}_Part{seg.Index}_en-zh.mp4");

                        string burnedPath = await _videoProcessingService
                            .BurnSubtitleAsync(originalSegPath, segSrtPath, segOutPath)
                            .ConfigureAwait(false);

                        finalSegmentPaths.Add(burnedPath);
                        continue;
                    }
                }

                task.Progress = 90;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 7. 合并四段视频为最终成品
                string outputFileName = $"{task.Name}_EasyCut.mp4";
                string mergedPath = await _videoProcessingService
                    .MergeSegmentsAsync(finalSegmentPaths, outputDirectory, outputFileName)
                    .ConfigureAwait(false);

                task.Progress = 100;
                task.Status = VideoTaskStatus.Completed;
                task.ErrorMessage = null;
                task.OutputFilePath = mergedPath;
                task.UpdatedTime = DateTime.Now;

                await _repository.UpdateAsync(task).ConfigureAwait(false);

                return task;
            }
            catch (Exception ex)
            {
                task.Status = VideoTaskStatus.Failed;
                task.ErrorMessage = ex.ToString(); // 保存完整异常信息便于排查
                task.UpdatedTime = DateTime.Now;

                await _repository.UpdateAsync(task).ConfigureAwait(false);
                return task;
            }
        }

        /// <inheritdoc />
        public Task<VideoTask?> GetTaskAsync(Guid id)
        {
            return _repository.GetByIdAsync(id);
        }

        /// <summary>
        /// 将视频时长平均分成四段。
        /// </summary>
        private static List<SegmentConfig> BuildFourSegments(double durationSeconds)
        {
            if (durationSeconds <= 0)
            {
                throw new ArgumentException("视频时长无效。", nameof(durationSeconds));
            }

            double quarter = durationSeconds / 4.0;

            double s1 = 0;
            double e1 = quarter;

            double s2 = e1;
            double e2 = quarter * 2;

            double s3 = e2;
            double e3 = quarter * 3;

            double s4 = e3;
            double e4 = durationSeconds;

            return new List<SegmentConfig>
            {
                new SegmentConfig { Index = 1, StartSeconds = s1, EndSeconds = e1 },
                new SegmentConfig { Index = 2, StartSeconds = s2, EndSeconds = e2 },
                new SegmentConfig { Index = 3, StartSeconds = s3, EndSeconds = e3 },
                new SegmentConfig { Index = 4, StartSeconds = s4, EndSeconds = e4 }
            };
        }

        /// <summary>
        /// 截取指定时间段内的字幕，并将时间轴重置为从 0 开始（匹配切分后片段）。
        /// </summary>
        private static IReadOnlyList<SrtEntry> SliceAndShift(
            IReadOnlyList<SrtEntry> allEntries,
            double segmentStartSeconds,
            double segmentEndSeconds)
        {
            var result = new List<SrtEntry>();

            if (allEntries.Count == 0 || segmentEndSeconds <= segmentStartSeconds)
            {
                return result;
            }

            var segStart = TimeSpan.FromSeconds(segmentStartSeconds);
            var segEnd = TimeSpan.FromSeconds(segmentEndSeconds);

            foreach (var e in allEntries)
            {
                if (e.End <= segStart || e.Start >= segEnd)
                {
                    continue;
                }

                var newStart = e.Start - segStart;
                var newEnd = e.End - segStart;

                if (newStart < TimeSpan.Zero)
                {
                    newStart = TimeSpan.Zero;
                }

                if (newEnd <= newStart)
                {
                    newEnd = newStart + TimeSpan.FromMilliseconds(500);
                }

                var clone = new SrtEntry
                {
                    Index = result.Count + 1,
                    Start = newStart,
                    End = newEnd
                };
                clone.Lines.AddRange(e.Lines);

                result.Add(clone);
            }

            return result;
        }
    }
}