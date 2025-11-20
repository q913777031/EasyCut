#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace EasyCut.Scripting
{
    /// <summary>
    /// 剧本仓储接口。
    /// </summary>
    public interface IScriptEpisodeRepository
    {
        /// <summary>
        /// 根据视频文件路径尝试加载对应的剧本信息。
        /// 约定：视频文件名中包含 SxxExx（例如 S01E01）。
        /// </summary>
        /// <param name="videoFilePath">视频文件完整路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在对应剧本则返回 <see cref="ScriptEpisode"/>，否则返回 null。</returns>
        Task<ScriptEpisode?> TryLoadByVideoAsync(
            string videoFilePath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 根据剧集代码（SxxExx，例如 S01E01）尝试加载对应剧本信息。
        /// </summary>
        /// <param name="episodeCode">剧集代码，形如 S01E01（大小写不敏感）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>存在对应剧本则返回 <see cref="ScriptEpisode"/>，否则返回 null。</returns>
        Task<ScriptEpisode?> TryLoadByEpisodeCodeAsync(
            string episodeCode,
            CancellationToken cancellationToken = default);
    }
}