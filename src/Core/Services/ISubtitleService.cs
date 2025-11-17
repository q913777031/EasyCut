#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;

namespace EasyCut.Core.Services
{
    /// <summary>
    /// 字幕服务接口
    /// </summary>
    public interface ISubtitleService
    {
        /// <summary>
        /// 尝试从视频文件中提取已有字幕（如内嵌字幕），如果不存在则返回 null
        /// </summary>
        Task<string?> TryExtractSubtitleAsync(string videoPath);

        /// <summary>
        /// 使用语音识别从视频中生成英文字幕文件（srt）
        /// </summary>
        Task<string> GenerateEnglishSubtitleAsync(string videoPath);

        /// <summary>
        /// 将英文字幕翻译为中文字幕，并返回中文字幕文件路径
        /// </summary>
        Task<string> TranslateSubtitleToChineseAsync(string englishSubtitlePath);

        /// <summary>
        /// 合并中英字幕，生成中英双语字幕文件路径
        /// </summary>
        Task<string> MergeEnglishAndChineseSubtitleAsync(string englishSubtitlePath, string chineseSubtitlePath);

        /// <summary>
        /// 按分段配置切分字幕，返回每个分段对应的字幕文件路径
        /// </summary>
        Task<IReadOnlyList<string>> SplitSubtitleBySegmentsAsync(
            string subtitlePath,
            IReadOnlyList<Models.SegmentConfig> segments);
    }
}