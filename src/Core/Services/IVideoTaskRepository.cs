#nullable enable

using System;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频任务仓储接口
    /// </summary>
    public interface IVideoTaskRepository
    {
        /// <summary>
        /// 写入新任务
        /// </summary>
        Task InsertAsync(VideoTask task);

        /// <summary>
        /// 更新任务
        /// </summary>
        Task UpdateAsync(VideoTask task);

        /// <summary>
        /// 按 Id 获取任务
        /// </summary>
        Task<VideoTask?> GetByIdAsync(Guid id);
    }
}