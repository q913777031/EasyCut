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
    /// - 截取原视频前 60 秒作为基础片段；
    /// - 抽取这 60 秒音频并用 Whisper 生成英文 / 中英字幕；
    /// - 基于同一个 60 秒片段制作四段：
    ///   1. 无字幕；
    ///   2. 叠英文字幕；
    ///   3. 叠中英字幕；
    ///   4. 无字幕；
    /// - 将四段有序拼接成 4 分钟成品视频；
    /// - 最终只保留成品，删除所有中间文件。
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

            // 记录所有中间文件路径，成功后统一删除
            var tempFiles = new List<string>();

            try
            {
                task.Status = VideoTaskStatus.Processing;
                task.Progress = 5;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 1. 获取视频总时长，至少要有 60 秒
                double totalDuration = await _videoProcessingService
                    .GetVideoDurationAsync(inputVideoPath)
                    .ConfigureAwait(false);

                if (totalDuration < 60.0)
                {
                    throw new InvalidOperationException("视频总时长不足 60 秒，无法生成 4 分钟输出。");
                }

                // 2. 从原视频中剪出前 60 秒作为基础片段
                // 这里复用 SplitVideoAsync，只构造一个 Segment：0~60 秒
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
                    .SplitVideoAsync(inputVideoPath, baseSegments, outputDirectory)
                    .ConfigureAwait(false);

                if (baseSegmentPaths.Count == 0)
                {
                    throw new InvalidOperationException("截取前 60 秒片段失败。");
                }

                string baseClipPath = baseSegmentPaths[0];
                tempFiles.Add(baseClipPath);

                task.Progress = 15;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 3. 抽取基础片段的音频（前 60 秒）并生成字幕
                // 注意：ExtractAudioAsync 已经使用 -t 60，只针对前 60 秒音频
                string audioPath = await _videoProcessingService
                    .ExtractAudioAsync(baseClipPath, outputDirectory, task.Name)
                    .ConfigureAwait(false);

                tempFiles.Add(audioPath);

                task.Progress = 25;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                var (englishSrtPath, bilingualSrtPath) =
                    await _subtitleGenerator.GenerateEnglishAndBilingualSrtAsync(
                            audioPath,
                            outputDirectory,
                            baseFileName: task.Name,
                            translateAsync: null)
                        .ConfigureAwait(false);

                tempFiles.Add(englishSrtPath);
                tempFiles.Add(bilingualSrtPath);

                task.Progress = 40;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 4. 构造四个 60 秒片段：
                //    1) 原片段无字幕
                //    2) 原片段 + 英文字幕
                //    3) 原片段 + 中英字幕
                //    4) 原片段无字幕（再复制一份，保证参数一致）
                var finalSegmentPaths = new List<string>();

                // 第一段：无字幕（直接用 baseClip）
                string part1Path = Path.Combine(
                    outputDirectory,
                    $"{task.Name}_Part1_raw.mp4");

                // 为避免后面合并时参数不一致，这里统一转一遍（可选，也可以直接 copy）
                // 如果你确定 baseClip 与后面带字视频编码参数完全一致，也可以直接：
                // File.Copy(baseClipPath, part1Path, overwrite: true);
                File.Copy(baseClipPath, part1Path, overwrite: true);
                tempFiles.Add(part1Path);
                finalSegmentPaths.Add(part1Path);

                task.Progress = 50;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 第二段：英文字幕
                string part2Srt = englishSrtPath; // 直接用全 60 秒英文字幕
                string part2Path = Path.Combine(
                    outputDirectory,
                    $"{task.Name}_Part2_en.mp4");

                string part2Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, part2Srt, part2Path)
                    .ConfigureAwait(false);

                tempFiles.Add(part2Burned);
                finalSegmentPaths.Add(part2Burned);

                task.Progress = 65;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 第三段：中英字幕
                string part3Srt = bilingualSrtPath; // 全 60 秒中英字幕
                string part3Path = Path.Combine(
                    outputDirectory,
                    $"{task.Name}_Part3_en-zh.mp4");

                string part3Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, part3Srt, part3Path)
                    .ConfigureAwait(false);

                tempFiles.Add(part3Burned);
                finalSegmentPaths.Add(part3Burned);

                task.Progress = 80;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 第四段：无字幕，再拷贝一份基础片段
                string part4Path = Path.Combine(
                    outputDirectory,
                    $"{task.Name}_Part4_raw.mp4");

                File.Copy(baseClipPath, part4Path, overwrite: true);
                tempFiles.Add(part4Path);
                finalSegmentPaths.Add(part4Path);

                task.Progress = 90;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 5. 合并四段 → 输出 4 分钟成品视频
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

                // 6. 成功后删除所有中间文件，只保留最终成品
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

                return task;
            }
            catch (Exception ex)
            {
                task.Status = VideoTaskStatus.Failed;
                task.ErrorMessage = ex.ToString();
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
    }
}
