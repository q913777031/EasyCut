#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频处理服务接口（基于 FFmpeg）
    /// </summary>
    public interface IVideoProcessingService
    {
        /// <summary>
        /// 获取视频总时长（秒）
        /// </summary>
        Task<double> GetVideoDurationAsync(string videoPath);

        /// <summary>
        /// 根据分段配置切分视频，返回每个分段的视频路径
        /// </summary>
        Task<IReadOnlyList<string>> SplitVideoAsync(
            string videoPath,
            IReadOnlyList<SegmentConfig> segments,
            string outputDirectory);

        /// <summary>
        /// 合并多个视频片段为一个输出文件
        /// </summary>
        Task<string> MergeSegmentsAsync(
            IReadOnlyList<string> segmentVideoPaths,
            string outputDirectory,
            string outputFileName);
    }
}