#nullable enable

using System;

namespace EasyCut.Services
{
    /// <summary>
    /// SRT 字幕条目。
    /// </summary>
    public sealed class SubtitleEntry
    {
        /// <summary>
        /// 索引编号。
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 开始时间。
        /// </summary>
        public TimeSpan Start { get; set; }

        /// <summary>
        /// 结束时间。
        /// </summary>
        public TimeSpan End { get; set; }

        /// <summary>
        /// 文本内容。
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// 时长（秒）。
        /// </summary>
        public double DurationSeconds => (End - Start).TotalSeconds;
    }
}