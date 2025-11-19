#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频任务协调服务：
    /// - 使用 Whisper 对整段视频生成字幕；
    /// - 根据字幕内容自动选取一段适合学习的片段；
    /// - 对该片段制作 4 遍学习视频（无字幕 / 英文 / 中英 / 无字幕）；
    /// - 自动生成片头并拼接完整学习版视频；
    /// - 清理中间文件，仅保留最终输出。
    /// </summary>
    public sealed class VideoTaskCoordinator : IVideoTaskCoordinator
    {
        /// <summary>
        /// 任务仓储。
        /// </summary>
        private readonly IVideoTaskRepository _repository;

        /// <summary>
        /// 视频处理服务。
        /// </summary>
        private readonly IVideoProcessingService _videoProcessingService;

        /// <summary>
        /// 字幕生成服务。
        /// </summary>
        private readonly ISubtitleGenerator _subtitleGenerator;

        /// <summary>
        /// 内存任务缓存（用于 UI 实时进度更新）。
        /// </summary>
        private readonly ConcurrentDictionary<Guid, VideoTask> _taskCache = new();

        /// <summary>
        /// 构造函数。
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

        /// <summary>
        /// 创建任务（只建任务，不执行）。
        /// </summary>
        public async Task<VideoTask> CreateTaskAsync(string inputVideoPath, string outputDirectory)
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
                Phase = VideoTaskPhase.Pending,
                Progress = 0,
                CreatedTime = now,
                UpdatedTime = now
            };

            await _repository.InsertAsync(task).ConfigureAwait(false);
            _taskCache[task.Id] = task;

            return task;
        }

        /// <summary>
        /// 执行指定任务 Id 对应的完整视频处理流程。
        /// </summary>
        public async Task RunTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            if (!_taskCache.TryGetValue(taskId, out var task))
            {
                var fromDb = await _repository.GetByIdAsync(taskId).ConfigureAwait(false);
                if (fromDb is null)
                {
                    throw new InvalidOperationException("任务不存在。");
                }

                task = fromDb;
                _taskCache[taskId] = task;
            }

            var tempFiles = new List<string>();

            try
            {
                // 进入处理状态
                UpdateOnUiThread(task, t =>
                {
                    t.Status = VideoTaskStatus.Processing;
                    t.Phase = VideoTaskPhase.ExtractingAudio;
                    t.Progress = 5;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 0. 获取视频总时长（用于自动选片段的兜底）
                var totalDuration = await _videoProcessingService
                    .GetVideoDurationAsync(task.InputVideoPath)
                    .ConfigureAwait(false);

                // 1. 抽取整段音频，供 Whisper 生成字幕
                var fullAudioPath = await _videoProcessingService
                    .ExtractAudioAsync(task.InputVideoPath, task.OutputDirectory, task.Name + "_full")
                    .ConfigureAwait(false);

                tempFiles.Add(fullAudioPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 15;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 2. Whisper 生成整段英文 / 中英字幕
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.GeneratingSubtitles;
                    t.Progress = 25;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                var (englishFullSrtPath, bilingualFullSrtPath) =
                    await _subtitleGenerator.GenerateEnglishAndBilingualSrtAsync(
                            fullAudioPath,
                            task.OutputDirectory,
                            baseFileName: task.Name + "_full",
                            translateAsync: null)
                        .ConfigureAwait(false);

                tempFiles.Add(englishFullSrtPath);
                tempFiles.Add(bilingualFullSrtPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 35;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 3. 解析字幕并自动选取一段适合学习的片段
                var subtitles = SrtHelper.Parse(englishFullSrtPath);

                var (clipStartSec, clipEndSec) = SrtHelper.PickBestSegment(
        subtitles,
        totalDurationSeconds: totalDuration,
        minDuration: 4.0,
        maxDuration: totalDuration, // 几乎不限制
        targetDuration: totalDuration / 10.0); // 比如“整段的 1/10”当作理想长度

                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.SplittingVideo;
                    t.Progress = 45;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 4. 按选取的时间范围从原视频截取基础片段
                var baseSegments = new List<SegmentConfig>
                {
                    new SegmentConfig
                    {
                        Index = 1,
                        StartSeconds = clipStartSec,
                        EndSeconds = clipEndSec
                    }
                };

                var baseSegmentPaths = await _videoProcessingService
                    .SplitVideoAsync(task.InputVideoPath, baseSegments, task.OutputDirectory)
                    .ConfigureAwait(false);

                if (baseSegmentPaths.Count == 0)
                {
                    throw new InvalidOperationException("截取学习片段失败。");
                }

                var baseClipPath = baseSegmentPaths[0];
                tempFiles.Add(baseClipPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 55;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 5. 从整段字幕中裁剪出该片段对应的字幕（并将时间平移到 0）
                var englishClipSrtPath = SrtHelper.CropAndShift(
                    englishFullSrtPath,
                    clipStartSec,
                    clipEndSec,
                    task.OutputDirectory,
                    $"{task.Name}_Clip_en.srt");

                var bilingualClipSrtPath = SrtHelper.CropAndShift(
                    bilingualFullSrtPath,
                    clipStartSec,
                    clipEndSec,
                    task.OutputDirectory,
                    $"{task.Name}_Clip_bi.srt");

                tempFiles.Add(englishClipSrtPath);
                tempFiles.Add(bilingualClipSrtPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 60;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 6. 构造四个片头卡片（黑底白字）
                const double titleDuration = 2.0;

                var intro1Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第一遍 无字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro1.mp4",
                    titleDuration).ConfigureAwait(false);

                var intro2Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第二遍 英文字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro2.mp4",
                    titleDuration).ConfigureAwait(false);

                var intro3Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第三遍 中英文字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro3.mp4",
                    titleDuration).ConfigureAwait(false);

                var intro4Path = await _videoProcessingService.CreateTitleCardAsync(
                    "最后一遍 无字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro4.mp4",
                    titleDuration).ConfigureAwait(false);

                tempFiles.Add(intro1Path);
                tempFiles.Add(intro2Path);
                tempFiles.Add(intro3Path);
                tempFiles.Add(intro4Path);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 70;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 7. 生成四遍视频片段：P1 无字幕、P2 英文字幕、P3 中英字幕、P4 无字幕
                var finalSegmentPaths = new List<string>();

                // Part1：无字幕（拷贝基础片段）
                var part1Path = Path.Combine(task.OutputDirectory, $"{task.Name}_Part1_raw.mp4");
                File.Copy(baseClipPath, part1Path, overwrite: true);
                tempFiles.Add(part1Path);

                // Part2：英文字幕
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.BurningSubtitlePart2;
                    t.Progress = 75;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                var part2Out = Path.Combine(task.OutputDirectory, $"{task.Name}_Part2_en.mp4");
                var part2Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, englishClipSrtPath, part2Out)
                    .ConfigureAwait(false);
                tempFiles.Add(part2Burned);

                // Part3：中英字幕
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.BurningSubtitlePart3;
                    t.Progress = 80;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                var part3Out = Path.Combine(task.OutputDirectory, $"{task.Name}_Part3_en-zh.mp4");
                var part3Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, bilingualClipSrtPath, part3Out)
                    .ConfigureAwait(false);
                tempFiles.Add(part3Burned);

                // Part4：无字幕（再拷贝一份基础片段）
                var part4Path = Path.Combine(task.OutputDirectory, $"{task.Name}_Part4_raw.mp4");
                File.Copy(baseClipPath, part4Path, overwrite: true);
                tempFiles.Add(part4Path);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 85;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 8. 最终拼接顺序：
                // Intro1 -> Part1 -> Intro2 -> Part2 -> Intro3 -> Part3 -> Intro4 -> Part4
                finalSegmentPaths.Add(intro1Path);
                finalSegmentPaths.Add(part1Path);
                finalSegmentPaths.Add(intro2Path);
                finalSegmentPaths.Add(part2Burned);
                finalSegmentPaths.Add(intro3Path);
                finalSegmentPaths.Add(part3Burned);
                finalSegmentPaths.Add(intro4Path);
                finalSegmentPaths.Add(part4Path);

                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.MergingSegments;
                    t.Progress = 90;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 9. 合并为最终成品视频
                var outputFileName = $"{task.Name}_EasyCut_Learning.mp4";
                var mergedPath = await _videoProcessingService
                    .MergeSegmentsAsync(finalSegmentPaths, task.OutputDirectory, outputFileName)
                    .ConfigureAwait(false);

                UpdateOnUiThread(task, t =>
                {
                    t.OutputFilePath = mergedPath;
                    t.Status = VideoTaskStatus.Completed;
                    t.Phase = VideoTaskPhase.Completed;
                    t.Progress = 100;
                    t.ErrorMessage = null;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 10. 清理中间文件
                foreach (var file in tempFiles)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // 删除失败忽略
                    }
                }
            }
            catch (OperationCanceledException)
            {
                UpdateOnUiThread(task, t =>
                {
                    t.Status = VideoTaskStatus.Failed;
                    t.Phase = VideoTaskPhase.Failed;
                    t.ErrorMessage = "任务已取消。";
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                UpdateOnUiThread(task, t =>
                {
                    t.Status = VideoTaskStatus.Failed;
                    t.Phase = VideoTaskPhase.Failed;
                    t.ErrorMessage = ex.ToString();
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 兼容用法：创建并执行任务（非 UI 场景可直接调用）。
        /// </summary>
        public async Task<VideoTask> CreateAndRunTaskAsync(string inputVideoPath, string outputDirectory)
        {
            var task = await CreateTaskAsync(inputVideoPath, outputDirectory).ConfigureAwait(false);
            await RunTaskAsync(task.Id).ConfigureAwait(false);
            return task;
        }

        /// <summary>
        /// 获取任务最新信息。
        /// </summary>
        public async Task<VideoTask?> GetTaskAsync(Guid id)
        {
            if (_taskCache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            var fromDb = await _repository.GetByIdAsync(id).ConfigureAwait(false);
            if (fromDb is not null)
            {
                _taskCache[id] = fromDb;
            }

            return fromDb;
        }

        /// <summary>
        /// 在 UI 线程上更新任务对象，确保线程安全。
        /// </summary>
        private static void UpdateOnUiThread(VideoTask task, Action<VideoTask> updateAction)
        {
            var app = Application.Current;
            if (app?.Dispatcher == null)
            {
                updateAction(task);
                return;
            }

            if (app.Dispatcher.CheckAccess())
            {
                updateAction(task);
            }
            else
            {
                app.Dispatcher.Invoke(() => updateAction(task));
            }
        }
    }
}