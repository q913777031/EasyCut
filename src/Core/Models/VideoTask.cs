#nullable enable

using System;

namespace EasyCut.Models
{
    /// <summary>
    /// 视频剪辑任务
    /// </summary>
    public class VideoTask
    {
        /// <summary>
        /// 任务 Id
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// 输入视频完整路径
        /// </summary>
        public string InputVideoPath { get; set; } = string.Empty;

        /// <summary>
        /// 输出目录
        /// </summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 任务名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 当前状态
        /// </summary>
        public VideoTaskStatus Status { get; set; }

        /// <summary>
        /// 进度百分比（0-100）
        /// </summary>
        public int Progress { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// 创建时间
        /// </summary>
        public DateTime CreatedTime { get; set; }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime UpdatedTime { get; set; }
    }

    /// <summary>
    /// 视频任务状态
    /// </summary>
    public enum VideoTaskStatus
    {
        /// <summary>
        /// 等待中
        /// </summary>
        Pending,

        /// <summary>
        /// 处理中
        /// </summary>
        Processing,

        /// <summary>
        /// 已完成
        /// </summary>
        Completed,

        /// <summary>
        /// 失败
        /// </summary>
        Failed
    }
}