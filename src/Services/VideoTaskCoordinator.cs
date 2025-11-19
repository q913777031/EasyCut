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
    /// - 截取原视频前 60 秒作为基础片段；
    /// - 抽取这 60 秒音频并用 Whisper 生成英文 / 中英字幕；
    /// - 生成四个中文片头（黑底白字）；
    /// - 顺序拼接生成学习版视频；
    /// - 只保留最终合并视频，其余中间文件自动清理。
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
        /// <param name="inputVideoPath">输入视频路径。</param>
        /// <param name="outputDirectory">输出目录。</param>
        /// <returns>新创建的任务。</returns>
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
                // 和你的原实现保持一致：用不带扩展名的文件名。
                Name = Path.GetFileNameWithoutExtension(inputVideoPath),
                Status = VideoTaskStatus.Pending,
                Phase = VideoTaskPhase.Pending,
                Progress = 0,
                CreatedTime = now,
                UpdatedTime = now
            };

            // 持久化一份
            await _repository.InsertAsync(task).ConfigureAwait(false);

            // 缓存在内存中，用于 UI 绑定实时更新
            _taskCache[task.Id] = task;

            return task;
        }

        /// <summary>
        /// 执行指定任务 Id 对应的完整视频处理流程。
        /// </summary>
        /// <param name="taskId">任务 Id。</param>
        /// <param name="cancellationToken">取消标记。</param>
        public async Task RunTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
        {
            if (!_taskCache.TryGetValue(taskId, out var task))
            {
                // 如果缓存中没有，尝试从仓储加载
                var fromDb = await _repository.GetByIdAsync(taskId).ConfigureAwait(false);
                if (fromDb is null)
                {
                    throw new InvalidOperationException("任务不存在。");
                }

                task = fromDb;
                _taskCache[taskId] = task;
            }

            // 所有中间文件路径，成功后统一删除
            var tempFiles = new List<string>();

            try
            {
                // 进入处理状态
                UpdateOnUiThread(task, t =>
                {
                    t.Status = VideoTaskStatus.Processing;
                    t.Phase = VideoTaskPhase.Pending;
                    t.Progress = 5;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 1. 获取总时长，至少要有 60 秒
                double totalDuration = await _videoProcessingService
                    .GetVideoDurationAsync(task.InputVideoPath)
                    .ConfigureAwait(false);

                if (totalDuration < 60.0)
                {
                    throw new InvalidOperationException("视频总时长不足 60 秒，无法生成 4 遍学习视频。");
                }

                // 2. 截取前 60 秒作为基础片段
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.SplittingVideo;
                    t.Progress = 10;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                var baseSegments = new List<SegmentConfig>
                {
                    new SegmentConfig
                    {
                        Index = 1,
                        StartSeconds = 0,
                        EndSeconds = 60
                    }
                };

                var baseSegmentPaths = await _videoProcessingService
                    .SplitVideoAsync(task.InputVideoPath, baseSegments, task.OutputDirectory)
                    .ConfigureAwait(false);

                if (baseSegmentPaths.Count == 0)
                {
                    throw new InvalidOperationException("截取前 60 秒片段失败。");
                }

                string baseClipPath = baseSegmentPaths[0];
                tempFiles.Add(baseClipPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 15;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 3. 抽取基础片段音频
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.ExtractingAudio;
                    t.Progress = 20;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                string audioPath = await _videoProcessingService
                    .ExtractAudioAsync(baseClipPath, task.OutputDirectory, task.Name)
                    .ConfigureAwait(false);

                tempFiles.Add(audioPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 25;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 4. Whisper 生成英文 / 中英字幕
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.GeneratingSubtitles;
                    t.Progress = 30;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                var (englishSrtPath, bilingualSrtPath) =
                    await _subtitleGenerator.GenerateEnglishAndBilingualSrtAsync(
                            audioPath,
                            task.OutputDirectory,
                            baseFileName: task.Name,
                            translateAsync: null)  // 先不翻译，中英字幕后续再接 AI
                        .ConfigureAwait(false);

                tempFiles.Add(englishSrtPath);
                tempFiles.Add(bilingualSrtPath);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 40;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 5. 构造四个片头卡片（黑底白字）
                const double titleDuration = 2.0; // 每个片头 2 秒，可按需调整

                string intro1Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第一遍 无字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro1.mp4",
                    titleDuration).ConfigureAwait(false);

                string intro2Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第二遍 英文字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro2.mp4",
                    titleDuration).ConfigureAwait(false);

                string intro3Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第三遍 中英文字幕",
                    task.OutputDirectory,
                    $"{task.Name}_Intro3.mp4",
                    titleDuration).ConfigureAwait(false);

                string intro4Path = await _videoProcessingService.CreateTitleCardAsync(
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
                    t.Progress = 50;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 6. 生成四遍视频片段
                var finalSegmentPaths = new List<string>();

                // Part1：无字幕（拷贝一份基础片段）
                string part1Path = Path.Combine(task.OutputDirectory, $"{task.Name}_Part1_raw.mp4");
                File.Copy(baseClipPath, part1Path, overwrite: true);
                tempFiles.Add(part1Path);

                // Part2：英文字幕
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.BurningSubtitlePart2;
                    t.Progress = 60;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                string part2Out = Path.Combine(task.OutputDirectory, $"{task.Name}_Part2_en.mp4");
                string part2Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, englishSrtPath, part2Out)
                    .ConfigureAwait(false);
                tempFiles.Add(part2Burned);

                // Part3：中英文字幕（目前“中英”实际上还是英文两遍占位，后续接翻译）
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.BurningSubtitlePart3;
                    t.Progress = 70;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                string part3Out = Path.Combine(task.OutputDirectory, $"{task.Name}_Part3_en-zh.mp4");
                string part3Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, bilingualSrtPath, part3Out)
                    .ConfigureAwait(false);
                tempFiles.Add(part3Burned);

                // Part4：无字幕（再拷贝一份基础片段）
                string part4Path = Path.Combine(task.OutputDirectory, $"{task.Name}_Part4_raw.mp4");
                File.Copy(baseClipPath, part4Path, overwrite: true);
                tempFiles.Add(part4Path);

                UpdateOnUiThread(task, t =>
                {
                    t.Progress = 75;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                cancellationToken.ThrowIfCancellationRequested();

                // 7. 最终拼接顺序（学习版）：
                // Intro1 -> Part1 -> Intro2 -> Part2 -> Intro3 -> Part3 -> Intro4 -> Part4
                finalSegmentPaths.Add(intro1Path);
                finalSegmentPaths.Add(part1Path);
                finalSegmentPaths.Add(intro2Path);
                finalSegmentPaths.Add(part2Burned);
                finalSegmentPaths.Add(intro3Path);
                finalSegmentPaths.Add(part3Burned);
                finalSegmentPaths.Add(intro4Path);
                finalSegmentPaths.Add(part4Path);

                // 8. 合并为最终成品视频
                UpdateOnUiThread(task, t =>
                {
                    t.Phase = VideoTaskPhase.MergingSegments;
                    t.Progress = 90;
                    t.UpdatedTime = DateTime.Now;
                });
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                string outputFileName = $"{task.Name}_EasyCut_Learning.mp4";
                string mergedPath = await _videoProcessingService
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

                // 9. 清理中间文件，只保留成品
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
        /// 兼容旧用法：创建并执行任务（用于非 UI 场景）。
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
            // 优先返回内存中的（UI 正在绑定的那份）
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
        /// <param name="task">任务对象。</param>
        /// <param name="updateAction">更新逻辑。</param>
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