#nullable enable

using System.Threading;
using System.Threading.Tasks;
using EasyCut.Scripting;

namespace EasyCut.Services
{
    /// <summary>
    /// PDF 剧本导入服务。
    /// </summary>
    public interface IPdfScriptImportService
    {
        /// <summary>
        /// 从指定 PDF 导入一集剧本。
        /// </summary>
        /// <param name="pdfPath">PDF 文件路径。</param>
        /// <param name="episodeId">剧集 Id（如 Sheldon-S01E01）。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        Task<ScriptEpisode> ImportAsync(
            string pdfPath,
            string episodeId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 将剧本保存为 JSON 文件（放到 EasyCut 的数据目录下）。
        /// </summary>
        Task SaveEpisodeAsync(
            ScriptEpisode episode,
            CancellationToken cancellationToken = default);
    }
}