#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频任务协调服务实现（真实四段切分 + 合并版本，暂不处理字幕）
    /// </summary>
    public class VideoTaskCoordinator : IVideoTaskCoordinator
    {
        private readonly IVideoTaskRepository _repository;
        private readonly IVideoProcessingService _videoProcessingService;

        /// <summary>
        /// 构造函数
        /// </summary>
        public VideoTaskCoordinator(
            IVideoTaskRepository repository,
            IVideoProcessingService videoProcessingService)
        {
            _repository = repository;
            _videoProcessingService = videoProcessingService;
        }

        /// <summary>
        /// 创建并执行一个任务
        /// </summary>
        public async Task<VideoTask> CreateAndRunTaskAsync(string inputVideoPath, string outputDirectory)
        {
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

            await _repository.InsertAsync(task);

            try
            {
                task.Status = VideoTaskStatus.Processing;
                task.Progress = 5;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);

                // 1. 获取总时长
                double duration = await _videoProcessingService.GetVideoDurationAsync(inputVideoPath);
                task.Progress = 15;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);

                // 2. 构造四段配置（等分）
                var segments = BuildFourSegments(duration);
                task.Progress = 25;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);

                // 3. 切分视频
                var segmentPaths = await _videoProcessingService.SplitVideoAsync(
                    inputVideoPath, segments, outputDirectory);
                task.Progress = 70;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);

                // 4. 合并视频（后续可以在这里加“不同字幕模式”的逻辑）
                string outputFileName = $"{task.Name}_EasyCut.mp4";
                string mergedPath = await _videoProcessingService.MergeSegmentsAsync(
                    segmentPaths, outputDirectory, outputFileName);

                task.Progress = 100;
                task.Status = VideoTaskStatus.Completed;
                task.ErrorMessage = null;
                task.UpdatedTime = DateTime.Now;

                await _repository.UpdateAsync(task);

                return task;
            }
            catch (Exception ex)
            {
                task.Status = VideoTaskStatus.Failed;

                // 保存完整异常信息，包含堆栈和 InnerException
                task.ErrorMessage = ex.ToString();

                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);
                return task;
            }
        }

        /// <summary>
        /// 查询任务
        /// </summary>
        public Task<VideoTask?> GetTaskAsync(Guid id)
        {
            return _repository.GetByIdAsync(id);
        }

        /// <summary>
        /// 将时长等分为四段
        /// </summary>
        private static List<SegmentConfig> BuildFourSegments(double durationSeconds)
        {
            if (durationSeconds <= 0)
            {
                throw new ArgumentException("视频时长无效", nameof(durationSeconds));
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
    }
}