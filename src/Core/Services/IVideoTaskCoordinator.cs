#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 任务协调服务接口（负责整个“电影 → 四段视频”的编排）
    /// </summary>
    public interface IVideoTaskCoordinator
    {
        /// <summary>
        /// 创建任务并持久化，但不开始执行。
        /// </summary>
        Task<VideoTask> CreateTaskAsync(string inputFilePath, string outputDirectory);

        /// <summary>
        /// 根据任务 Id 执行任务，执行过程中会持续更新任务对象的状态和进度。
        /// </summary>
        Task RunTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

        /// <summary>
        /// 按任务 Id 查询当前任务状态
        /// </summary>
        Task<VideoTask?> GetTaskAsync(Guid taskId);
    }
}