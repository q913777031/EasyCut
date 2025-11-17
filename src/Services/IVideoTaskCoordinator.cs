#nullable enable

using System;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频任务协调服务接口
    /// </summary>
    public interface IVideoTaskCoordinator
    {
        /// <summary>
        /// 创建并执行一个新任务
        /// </summary>
        Task<VideoTask> CreateAndRunTaskAsync(string inputVideoPath, string outputDirectory);

        /// <summary>
        /// 查询任务
        /// </summary>
        Task<VideoTask?> GetTaskAsync(Guid id);
    }
}