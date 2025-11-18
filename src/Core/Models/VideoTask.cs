#nullable enable

using System;

namespace EasyCut.Models
{
    /// <summary>
    /// 视频剪辑任务
    /// </summary>
    public class VideoTask
    {
        public Guid Id { get; set; }

        public string InputVideoPath { get; set; } = string.Empty;

        public string OutputDirectory { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 当前状态（等待/处理中/完成/失败）
        /// </summary>
        public VideoTaskStatus Status { get; set; }

        /// <summary>
        /// 总体进度百分比（0-100）
        /// </summary>
        public int Progress { get; set; }

        /// <summary>
        /// 当前处理阶段
        /// </summary>
        public VideoTaskPhase Phase { get; set; } = VideoTaskPhase.Pending;

        /// <summary>
        /// 最终生成的视频完整路径（成功时写入）
        /// </summary>
        public string? OutputFilePath { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string? ErrorMessage { get; set; }

        public DateTime CreatedTime { get; set; }

        public DateTime UpdatedTime { get; set; }

        /// <summary>
        /// 阶段显示名称（方便 XAML 直接绑定）
        /// </summary>
        public string PhaseDisplay =>
            Phase switch
            {
                VideoTaskPhase.Pending => "等待中",
                VideoTaskPhase.ExtractingAudio => "抽取音频",
                VideoTaskPhase.GeneratingSubtitles => "生成字幕",
                VideoTaskPhase.SplittingVideo => "切分视频",
                VideoTaskPhase.BurningSubtitlePart2 => "烧入英文字幕（第2段）",
                VideoTaskPhase.BurningSubtitlePart3 => "烧入中英字幕（第3段）",
                VideoTaskPhase.MergingSegments => "合并视频",
                VideoTaskPhase.Completed => "完成",
                VideoTaskPhase.Failed => "失败",
                _ => "未知"
            };
    }

    public enum VideoTaskStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }

    /// <summary>
    /// 细分阶段
    /// </summary>
    public enum VideoTaskPhase
    {
        Pending = 0,

        /// <summary>
        /// 抽取音频
        /// </summary>
        ExtractingAudio,

        /// <summary>
        /// Whisper 生成字幕
        /// </summary>
        GeneratingSubtitles,

        /// <summary>
        /// 切分视频为四段
        /// </summary>
        SplittingVideo,

        /// <summary>
        /// 第 2 段烧入英文字幕
        /// </summary>
        BurningSubtitlePart2,

        /// <summary>
        /// 第 3 段烧入中英字幕
        /// </summary>
        BurningSubtitlePart3,

        /// <summary>
        /// 合并四段
        /// </summary>
        MergingSegments,

        Completed,
        Failed
    }
}
