#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using EasyCut.Core.Models;

namespace EasyCut.Core.Services
{
    /// <summary>
    /// 视频处理服务接口
    /// </summary>
    public interface IVideoProcessingService
    {
        /// <summary>
        /// 读取视频信息，返回视频总时长等基础信息
        /// </summary>
        Task<double> GetVideoDurationAsync(string videoPath);

        /// <summary>
        /// 根据分段配置切分视频，返回每段临时视频路径列表
        /// </summary>
        Task<IReadOnlyList<string>> SplitVideoAsync(string videoPath, IReadOnlyList<SegmentConfig> segments);

        /// <summary>
        /// 给指定视频片段嵌入字幕（可选择无字幕、英文、中英）
        /// </summary>
        Task<string> EmbedSubtitleAsync(
            string segmentVideoPath,
            string? subtitlePath,
            SubtitleMode mode);

        /// <summary>
        /// 合并多个视频片段为一个输出文件
        /// </summary>
        Task<string> MergeSegmentsAsync(IReadOnlyList<string> segmentVideoPaths, string outputDirectory);
    }
}