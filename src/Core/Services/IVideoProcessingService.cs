#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using EasyCut.Models;

namespace EasyCut.Services
{
    /// <summary>
    /// 视频处理服务接口（封装 ffmpeg / ffprobe 调用）
    /// </summary>
    public interface IVideoProcessingService
    {
        /// <summary>
        /// 获取视频总时长（秒）
        /// </summary>
        Task<double> GetVideoDurationAsync(string videoPath);

        /// <summary>
        /// 按指定时间段切分视频，返回每段生成的视频路径列表。
        /// </summary>
        Task<IReadOnlyList<string>> SplitVideoAsync(
            string videoPath,
            IReadOnlyList<SegmentConfig> segments,
            string outputDirectory);

        /// <summary>
        /// 将多个视频片段按顺序无重编码合并为一个输出文件。
        /// </summary>
        Task<string> MergeSegmentsAsync(
            IReadOnlyList<string> segmentVideoPaths,
            string outputDirectory,
            string outputFileName);

        /// <summary>
        /// 从视频中抽取音频为 16k 单声道 wav 文件。
        /// </summary>
        Task<string> ExtractAudioAsync(
            string videoPath,
            string outputDirectory,
            string baseFileName);

        /// <summary>
        /// 在视频上烧入字幕（硬字幕），返回新视频路径。
        /// </summary>
        Task<string> BurnSubtitleAsync(
            string inputVideoPath,
            string subtitlePath,
            string outputVideoPath);
    }
}