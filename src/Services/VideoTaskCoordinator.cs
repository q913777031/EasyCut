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
    /// - 生成四个中文片头（黑底白字）：
    ///   1. 第一遍 无字幕
    ///   2. 第二遍 英文字幕
    ///   3. 第三遍 中英文字幕
    ///   4. 最后一遍 无字幕
    /// - 顺序拼接：片头1 + 遍1 + 片头2 + 遍2 + 片头3 + 遍3 + 片头4 + 遍4；
    /// - 只保留最终合并视频，其余中间文件自动清理。
    /// </summary>
    public sealed class VideoTaskCoordinator : IVideoTaskCoordinator
    {
        private readonly IVideoTaskRepository _repository;
        private readonly IVideoProcessingService _videoProcessingService;
        private readonly ISubtitleGenerator _subtitleGenerator;

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

            // 所有中间文件路径，成功后统一删除
            var tempFiles = new List<string>();

            try
            {
                task.Status = VideoTaskStatus.Processing;
                task.Progress = 5;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 1. 获取总时长，至少要有 60 秒
                double totalDuration = await _videoProcessingService
                    .GetVideoDurationAsync(inputVideoPath)
                    .ConfigureAwait(false);

                if (totalDuration < 60.0)
                {
                    throw new InvalidOperationException("视频总时长不足 60 秒，无法生成 4 遍学习视频。");
                }

                // 2. 截取前 60 秒作为基础片段
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

                // 3. 抽取基础片段音频并生成字幕（不考虑翻译时，中英其实还是英文）
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
                            translateAsync: null)  // 先不翻译，中英字幕后续再接 AI
                        .ConfigureAwait(false);

                tempFiles.Add(englishSrtPath);
                tempFiles.Add(bilingualSrtPath);

                task.Progress = 40;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 4. 构造四个片头卡片（黑底白字）
                const double titleDuration = 2.0; // 每个片头 2 秒，可按需调整

                string intro1Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第一遍 无字幕",
                    outputDirectory,
                    $"{task.Name}_Intro1.mp4",
                    titleDuration).ConfigureAwait(false);

                string intro2Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第二遍 英文字幕",
                    outputDirectory,
                    $"{task.Name}_Intro2.mp4",
                    titleDuration).ConfigureAwait(false);

                string intro3Path = await _videoProcessingService.CreateTitleCardAsync(
                    "第三遍 中英文字幕",
                    outputDirectory,
                    $"{task.Name}_Intro3.mp4",
                    titleDuration).ConfigureAwait(false);

                string intro4Path = await _videoProcessingService.CreateTitleCardAsync(
                    "最后一遍 无字幕",
                    outputDirectory,
                    $"{task.Name}_Intro4.mp4",
                    titleDuration).ConfigureAwait(false);

                tempFiles.Add(intro1Path);
                tempFiles.Add(intro2Path);
                tempFiles.Add(intro3Path);
                tempFiles.Add(intro4Path);

                task.Progress = 50;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 5. 生成四遍视频片段：
                //    Part1: 无字幕
                //    Part2: 英文字幕
                //    Part3: 中英文字幕
                //    Part4: 无字幕

                var finalSegmentPaths = new List<string>();

                // Part1：无字幕（拷贝一份基础片段）
                string part1Path = Path.Combine(outputDirectory, $"{task.Name}_Part1_raw.mp4");
                File.Copy(baseClipPath, part1Path, overwrite: true);
                tempFiles.Add(part1Path);

                // Part2：英文字幕
                string part2Out = Path.Combine(outputDirectory, $"{task.Name}_Part2_en.mp4");
                string part2Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, englishSrtPath, part2Out)
                    .ConfigureAwait(false);
                tempFiles.Add(part2Burned);

                // Part3：中英文字幕（目前“中英”实际上还是英文两遍占位，后续接翻译）
                string part3Out = Path.Combine(outputDirectory, $"{task.Name}_Part3_en-zh.mp4");
                string part3Burned = await _videoProcessingService
                    .BurnSubtitleAsync(baseClipPath, bilingualSrtPath, part3Out)
                    .ConfigureAwait(false);
                tempFiles.Add(part3Burned);

                // Part4：无字幕（再拷贝一份基础片段）
                string part4Path = Path.Combine(outputDirectory, $"{task.Name}_Part4_raw.mp4");
                File.Copy(baseClipPath, part4Path, overwrite: true);
                tempFiles.Add(part4Path);

                task.Progress = 75;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 6. 最终拼接顺序（学习版）：
                // Intro1 -> Part1 -> Intro2 -> Part2 -> Intro3 -> Part3 -> Intro4 -> Part4
                finalSegmentPaths.Add(intro1Path);
                finalSegmentPaths.Add(part1Path);
                finalSegmentPaths.Add(intro2Path);
                finalSegmentPaths.Add(part2Burned);
                finalSegmentPaths.Add(intro3Path);
                finalSegmentPaths.Add(part3Burned);
                finalSegmentPaths.Add(intro4Path);
                finalSegmentPaths.Add(part4Path);

                // 7. 合并为最终成品视频
                string outputFileName = $"{task.Name}_EasyCut_Learning.mp4";
                string mergedPath = await _videoProcessingService
                    .MergeSegmentsAsync(finalSegmentPaths, outputDirectory, outputFileName)
                    .ConfigureAwait(false);

                task.Progress = 100;
                task.Status = VideoTaskStatus.Completed;
                task.ErrorMessage = null;
                task.OutputFilePath = mergedPath;
                task.UpdatedTime = DateTime.Now;

                await _repository.UpdateAsync(task).ConfigureAwait(false);

                // 8. 清理中间文件，只保留成品
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

        public Task<VideoTask?> GetTaskAsync(Guid id)
        {
            return _repository.GetByIdAsync(id);
        }
    }
}