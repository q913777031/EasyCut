#nullable enable

using System;

namespace EasyCut.Core.Models
{
    /// <summary>
    /// 单个视频分段配置
    /// </summary>
    public class SegmentConfig
    {
        /// <summary>
        /// 段序号（1-4）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 起始时间（秒）
        /// </summary>
        public double StartSeconds { get; set; }

        /// <summary>
        /// 结束时间（秒）
        /// </summary>
        public double EndSeconds { get; set; }

        /// <summary>
        /// 字幕模式
        /// </summary>
        public SubtitleMode SubtitleMode { get; set; }
    }

    /// <summary>
    /// 字幕模式枚举
    /// </summary>
    public enum SubtitleMode
    {
        /// <summary>
        /// 无字幕
        /// </summary>
        None,

        /// <summary>
        /// 英文字幕
        /// </summary>
        English,

        /// <summary>
        /// 中英字幕
        /// </summary>
        EnglishChinese
    }
}