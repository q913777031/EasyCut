#nullable enable

using System;
using Prism.Mvvm;

namespace EasyCut.Models
{
    /// <summary>
    /// 视频剪辑任务。
    /// </summary>
    public sealed class VideoTask : BindableBase
    {
        /// <summary>
        /// 任务 Id。
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// 输入视频路径。
        /// </summary>
        public string InputVideoPath { get; set; } = string.Empty;

        /// <summary>
        /// 输出目录。
        /// </summary>
        public string OutputDirectory { get; set; } = string.Empty;

        /// <summary>
        /// 任务名称（通常为视频文件名）。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 任务状态。
        /// </summary>
        public VideoTaskStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private VideoTaskStatus _status;

        /// <summary>
        /// 进度百分比（0-100）。
        /// </summary>
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private double _progress;

        /// <summary>
        /// 当前处理阶段。
        /// </summary>
        public VideoTaskPhase Phase
        {
            get => _phase;
            set
            {
                if (SetProperty(ref _phase, value))
                {
                    // 阶段变更时，通知 PhaseDisplay 也变化
                    RaisePropertyChanged(nameof(PhaseDisplay));
                }
            }
        }

        private VideoTaskPhase _phase = VideoTaskPhase.Pending;

        /// <summary>
        /// 输出文件路径。
        /// </summary>
        public string? OutputFilePath
        {
            get => _outputFilePath;
            set => SetProperty(ref _outputFilePath, value);
        }

        private string? _outputFilePath;

        /// <summary>
        /// 错误信息。
        /// </summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private string? _errorMessage;

        /// <summary>
        /// 创建时间。
        /// </summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 最后更新时间。
        /// </summary>
        public DateTime UpdatedTime
        {
            get => _updatedTime;
            set => SetProperty(ref _updatedTime, value);
        }

        private DateTime _updatedTime = DateTime.Now;

        /// <summary>
        /// 阶段显示名称（方便 XAML 直接绑定）。
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

    /// <summary>
    /// 任务总体状态。
    /// </summary>
    public enum VideoTaskStatus
    {
        /// <summary>
        /// 等待中。
        /// </summary>
        Pending,

        /// <summary>
        /// 处理中。
        /// </summary>
        Processing,

        /// <summary>
        /// 已完成。
        /// </summary>
        Completed,

        /// <summary>
        /// 失败。
        /// </summary>
        Failed
    }

    /// <summary>
    /// 细分处理阶段。
    /// </summary>
    public enum VideoTaskPhase
    {
        /// <summary>
        /// 等待中。
        /// </summary>
        Pending = 0,

        /// <summary>
        /// 抽取音频。
        /// </summary>
        ExtractingAudio,

        /// <summary>
        /// Whisper 生成字幕。
        /// </summary>
        GeneratingSubtitles,

        /// <summary>
        /// 切分视频为四段。
        /// </summary>
        SplittingVideo,

        /// <summary>
        /// 第 2 段烧入英文字幕。
        /// </summary>
        BurningSubtitlePart2,

        /// <summary>
        /// 第 3 段烧入中英字幕。
        /// </summary>
        BurningSubtitlePart3,

        /// <summary>
        /// 合并四段。
        /// </summary>
        MergingSegments,

        /// <summary>
        /// 已完成。
        /// </summary>
        Completed,

        /// <summary>
        /// 失败。
        /// </summary>
        Failed
    }
}