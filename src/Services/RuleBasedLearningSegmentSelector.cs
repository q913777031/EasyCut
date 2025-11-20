using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// 学习片段选择器接口。
    /// </summary>
    public interface ILearningSegmentSelector
    {
        /// <summary>
        /// 从整段字幕中选择适合学习的片段时间范围。
        /// </summary>
        /// <param name="subtitles">字幕条目集合。</param>
        /// <param name="totalDurationSeconds">视频总时长（秒）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>片段起始和结束时间（秒）。</returns>
        Task<(double StartSeconds, double EndSeconds)> SelectAsync(
            IReadOnlyList<SrtEntry> subtitles,
            double totalDurationSeconds,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 规则版学习片段选择器（直接调用现有 SrtHelper.PickBestSegment）。
    /// </summary>
    public sealed class RuleBasedLearningSegmentSelector : ILearningSegmentSelector
    {
        /// <summary>
        /// 选择学习片段时间范围。
        /// </summary>
        public Task<(double StartSeconds, double EndSeconds)> SelectAsync(
            IReadOnlyList<SrtEntry> subtitles,
            double totalDurationSeconds,
            CancellationToken cancellationToken = default)
        {
            if (totalDurationSeconds <= 0)
            {
                totalDurationSeconds = 1.0;
            }

            // 这里你可以根据喜好调参数
            var result = SrtHelper.PickBestSegment(
                subtitles,
                totalDurationSeconds: totalDurationSeconds,
                minDuration: 4.0,
                maxDuration: Math.Min(totalDurationSeconds, 45.0),
                targetDuration: 18.0);

            return Task.FromResult(result);
        }
    }
}