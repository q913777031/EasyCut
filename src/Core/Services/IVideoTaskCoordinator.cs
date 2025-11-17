#nullable enable

using System;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Core.Services
{
    /// <summary>
    /// 任务协调服务接口（负责整个“电影 → 四段视频”的编排）
    /// </summary>
    public interface IVideoTaskCoordinator
    {
        /// <summary>
        /// 创建并启动一个新任务（全自动流程）
        /// </summary>
        Task<VideoTask> CreateAndRunTaskAsync(
            string inputVideoPath,
            string outputDirectory);

        /// <summary>
        /// 按任务 Id 查询当前任务状态
        /// </summary>
        Task<VideoTask?> GetTaskAsync(Guid taskId);
    }
}