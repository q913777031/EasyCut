#nullable enable

using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EasyCut.Models;
using EasyCut.Scripting;

namespace EasyCut.Services
{
    /// <summary>
    /// 基于 json 文件的剧本仓储实现。
    /// <para>
    /// 约定：
    /// <list type="bullet">
    /// <item>剧本根目录：{BaseDirectory}\ScriptsEpisodes</item>
    /// <item>json 文件命名：SxxExx.json，例如 S01E01.json</item>
    /// <item>pdf 文件命名中包含 SxxExx（例如：小谢尔顿-S01E01.pdf）</item>
    /// <item>视频文件命名中也包含 SxxExx（例如：Young.Sheldon.S01E01.720p....mkv）</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class JsonScriptEpisodeRepository : IScriptEpisodeRepository
    {
        /// <summary>
        /// 剧本根目录。
        /// </summary>
        private readonly string _scriptsRoot;

        /// <summary>
        /// pdf→json 导入器。
        /// </summary>
        private readonly PdfScriptEpisodeImporter _importer;

        /// <summary>
        /// 匹配剧集代码的正则，例如 S01E01。
        /// </summary>
        private static readonly Regex EpisodeCodeRegex =
            new Regex(@"S\d{2}E\d{2}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="importer">pdf 剧本导入器。</param>
        public JsonScriptEpisodeRepository(PdfScriptEpisodeImporter importer)
        {
            _importer = importer ?? throw new ArgumentNullException(nameof(importer));

            _scriptsRoot = Path.Combine(AppContext.BaseDirectory, "ScriptsEpisodes");
            Directory.CreateDirectory(_scriptsRoot);
        }

        /// <summary>
        /// 根据视频文件路径尝试加载对应的剧本信息。
        /// 约定：视频文件名中包含 SxxExx（例如 S01E01）。
        /// </summary>
        public async Task<ScriptEpisode?> TryLoadByVideoAsync(
            string videoFilePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(videoFilePath))
            {
                return null;
            }

            var fileName = Path.GetFileNameWithoutExtension(videoFilePath);
            var episodeCode = TryGetEpisodeCodeFromFileName(fileName);
            if (string.IsNullOrWhiteSpace(episodeCode))
            {
                return null;
            }

            // 复用按剧集代码加载的逻辑
            return await TryLoadByEpisodeCodeAsync(episodeCode, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// 根据剧集代码（SxxExx，例如 S01E01）尝试加载对应剧本信息。
        /// </summary>
        public async Task<ScriptEpisode?> TryLoadByEpisodeCodeAsync(
            string episodeCode,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(episodeCode))
            {
                return null;
            }

            // 统一转成大写，保证文件名规范为 S01E01.json
            var upperCode = episodeCode.Trim().ToUpperInvariant();

            // 1. 先得到 json 路径
            var jsonPath = _importer.GetJsonPathByEpisodeCode(upperCode);

            // 2. 如果 json 不存在，尝试使用 pdf 自动导入
            if (!File.Exists(jsonPath))
            {
                var pdfPath = FindPdfByEpisodeCode(upperCode);
                if (pdfPath is not null)
                {
                    // 如果 pdf 存在，会在导入时生成 json，并返回 ScriptEpisode
                    var imported = await _importer
                        .ImportSingleIfNeededAsync(pdfPath, cancellationToken)
                        .ConfigureAwait(false);

                    if (imported is not null)
                    {
                        return imported;
                    }
                }

                // 没有 pdf 或导入失败
                return null;
            }

            // 3. json 存在，直接反序列化
            await using var fs = File.OpenRead(jsonPath);
            var episode = await JsonSerializer
                .DeserializeAsync<ScriptEpisode>(fs, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return episode;
        }

        /// <summary>
        /// 从文件名中提取 SxxExx 剧集代码。
        /// </summary>
        private static string? TryGetEpisodeCodeFromFileName(string fileNameWithoutExtension)
        {
            var match = EpisodeCodeRegex.Match(fileNameWithoutExtension);
            if (!match.Success)
            {
                return null;
            }

            return match.Value.ToUpperInvariant();
        }

        /// <summary>
        /// 在 ScriptsEpisodes 目录中查找包含指定剧集代码的 pdf。
        /// </summary>
        private string? FindPdfByEpisodeCode(string episodeCode)
        {
            if (!Directory.Exists(_scriptsRoot))
            {
                return null;
            }

            var upper = episodeCode.ToUpperInvariant();
            var pdfFiles = Directory.GetFiles(_scriptsRoot, "*.pdf", SearchOption.TopDirectoryOnly);

            foreach (var pdf in pdfFiles)
            {
                var name = Path.GetFileNameWithoutExtension(pdf);
                if (name.IndexOf(upper, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return pdf;
                }
            }

            return null;
        }
    }
}