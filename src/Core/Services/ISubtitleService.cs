#nullable enable

using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// 字幕服务接口
    /// </summary>
    public interface ISubtitleService
    {
        /// <summary>
        /// 获取英文字幕文件路径：
        /// 默认规则：视频同目录下、同文件名的 .srt
        /// </summary>
        Task<string> GetEnglishSubtitleAsync(string videoPath, string outputDirectory);

        /// <summary>
        /// 根据英文字幕生成中英双语字幕（当前先做“伪中英”结构，以后替换翻译逻辑）
        /// </summary>
        Task<string> GetBilingualSubtitleAsync(string englishSubtitlePath, string outputDirectory);

        /// <summary>
        /// 从完整字幕中裁剪出某个时间段对应的字幕文件（时间将从 0 开始重置）
        /// </summary>
        Task<string> CreateSegmentSubtitleAsync(
            string sourceSubtitlePath,
            double segmentStartSeconds,
            double segmentEndSeconds,
            string outputSubtitlePath);
    }
}