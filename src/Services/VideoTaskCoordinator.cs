#nullable enable

using System;
using System.IO;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 简化版任务协调服务（当前只做文件复制，后续替换为真正剪辑）
    /// </summary>
    public class VideoTaskCoordinator : IVideoTaskCoordinator
    {
        private readonly IVideoTaskRepository _repository;

        /// <summary>
        /// 构造函数
        /// </summary>
        public VideoTaskCoordinator(IVideoTaskRepository repository)
        {
            _repository = repository;
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
                task.Progress = 10;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);

                // 模拟处理耗时
                await Task.Delay(1000);

                // 暂时：复制原始文件作为“处理结果”
                Directory.CreateDirectory(outputDirectory);
                string destPath = Path.Combine(outputDirectory, $"{task.Name}_EasyCut.mp4");
                File.Copy(inputVideoPath, destPath, overwrite: true);

                task.Progress = 100;
                task.Status = VideoTaskStatus.Completed;
                task.UpdatedTime = DateTime.Now;
                await _repository.UpdateAsync(task);

                return task;
            }
            catch (Exception ex)
            {
                task.Status = VideoTaskStatus.Failed;
                task.ErrorMessage = ex.Message;
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
    }
}