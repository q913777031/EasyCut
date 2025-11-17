#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 内存版任务仓储实现（方便快速跑通，不依赖数据库）
    /// </summary>
    public class InMemoryVideoTaskRepository : IVideoTaskRepository
    {
        private readonly ConcurrentDictionary<Guid, VideoTask> _storage = new();

        /// <summary>
        /// 写入新任务
        /// </summary>
        public Task InsertAsync(VideoTask task)
        {
            _storage[task.Id] = task;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 更新任务
        /// </summary>
        public Task UpdateAsync(VideoTask task)
        {
            _storage[task.Id] = task;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 按 Id 获取任务
        /// </summary>
        public Task<VideoTask?> GetByIdAsync(Guid id)
        {
            _storage.TryGetValue(id, out var task);
            return Task.FromResult(task);
        }
    }
}