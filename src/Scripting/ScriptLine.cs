#nullable enable

using System;
using System.Collections.Generic;

namespace EasyCut.Scripting
{
    /// <summary>
    /// 剧本中的一行台词（带时间戳）。
    /// </summary>
    public sealed class ScriptLine
    {
        /// <summary>
        /// 开始时间（秒）。
        /// </summary>
        public double StartSeconds { get; set; }

        /// <summary>
        /// 结束时间（秒）。
        /// </summary>
        public double EndSeconds { get; set; }

        /// <summary>
        /// 英文台词。
        /// </summary>
        public string English { get; set; } = string.Empty;

        /// <summary>
        /// 中文翻译。
        /// </summary>
        public string Chinese { get; set; } = string.Empty;

        /// <summary>
        /// 关键单词列表。
        /// </summary>
        public List<string> Keywords { get; set; } = new();
    }

    /// <summary>
    /// 预设学习片段配置（由多行 ScriptLine 组成）。
    /// </summary>
    public sealed class LearningSegmentConfig
    {
        /// <summary>
        /// 片段 Id（例如：S01E01-001）。
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// 片段标题（例如：火车 + 牛顿定律）。
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// 片段开始时间（秒）。
        /// </summary>
        public double StartSeconds { get; set; }

        /// <summary>
        /// 片段结束时间（秒）。
        /// </summary>
        public double EndSeconds { get; set; }

        /// <summary>
        /// 片段内关键单词。
        /// </summary>
        public List<string> Keywords { get; set; } = new();

        /// <summary>
        /// 片段预览文本（用于 UI 列表中展示）。
        /// </summary>
        public string PreviewText { get; set; } = string.Empty;
    }

    /// <summary>
    /// 一集剧情（结合 PDF 剧本）数据。
    /// </summary>
    public sealed class ScriptEpisode
    {
        private string _episodeId = string.Empty;

        /// <summary>
        /// 剧集标识，例如 "S01E01"。
        /// 既用作 EpisodeId，也可作为 EpisodeCode。
        /// </summary>
        public string EpisodeId
        {
            get => _episodeId;
            set
            {
                _episodeId = value;
                if (string.IsNullOrWhiteSpace(EpisodeCode))
                {
                    EpisodeCode = value;
                }
            }
        }

        /// <summary>
        /// 剧集代码，例如 S01E01（用于和视频文件名匹配）。
        /// </summary>
        public string EpisodeCode { get; set; } = string.Empty;

        /// <summary>
        /// 剧集标题（可选，例如 小谢尔顿-S01E01）。
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 源 PDF 文件名（不带路径）。
        /// </summary>
        public string? SourcePdfFileName { get; set; }

        /// <summary>
        /// 原始剧本文本（整篇 pdf 抽取出来，便于后续人工或 AI 分析）。
        /// </summary>
        public string? RawText { get; set; }

        /// <summary>
        /// 逐行台词集合。
        /// </summary>
        public List<ScriptLine> Lines { get; set; } = new();

        /// <summary>
        /// 预设学习片段集合。
        /// </summary>
        public List<LearningSegmentConfig> Segments { get; set; } = new();

        // 如果你之前已经有 ScriptSegment 相关代码，还想兼容老结构，
        // 可以在这里额外加一个 List<ScriptSegment> OldSegments { get; set; } = new();
        // 先保留，不在导入器里使用即可。
    }

    /// <summary>
    /// 单个学习片段（起止时间 + 文本）。
    /// </summary>
    public sealed class ScriptSegment
    {
        /// <summary>
        /// 片段开始时间（秒）。
        /// </summary>
        public double StartSeconds { get; set; }

        /// <summary>
        /// 片段结束时间（秒）。
        /// </summary>
        public double EndSeconds { get; set; }

        /// <summary>
        /// 英文台词。
        /// </summary>
        public string English { get; set; } = string.Empty;

        /// <summary>
        /// 中文翻译（可选）。
        /// </summary>
        public string? Chinese { get; set; }

        /// <summary>
        /// 关键单词（可选，用于检索/高亮）。
        /// </summary>
        public List<string> Keywords { get; set; } = new();
    }
}