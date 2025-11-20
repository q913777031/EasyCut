#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EasyCut.Models;
using EasyCut.Scripting;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EasyCut.Services
{
    /// <summary>
    /// 剧本导入器：扫描 ScriptsEpisodes 目录下的 pdf，
    /// 根据文件名中的 SxxExx 生成对应的 json（ScriptEpisode）。
    /// 例如：
    ///   小谢尔顿-S01E01.pdf  ->  S01E01.json
    /// </summary>
    public sealed class PdfScriptEpisodeImporter
    {
        /// <summary>
        /// 剧本根目录（默认 {BaseDirectory}\ScriptsEpisodes）。
        /// </summary>
        private readonly string _scriptsRoot;

        /// <summary>
        /// 匹配剧集代码的正则，例如 S01E01。
        /// </summary>
        private static readonly Regex EpisodeCodeRegex =
            new Regex(@"S\d{2}E\d{2}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// 构造函数：使用默认剧本目录。
        /// </summary>
        public PdfScriptEpisodeImporter()
        {
            _scriptsRoot = Path.Combine(AppContext.BaseDirectory, "ScriptsEpisodes");
            Directory.CreateDirectory(_scriptsRoot);
        }

        /// <summary>
        /// 扫描 ScriptsEpisodes 目录下的 pdf 并导入为 json。
        /// 只会对尚未存在 json 文件的 pdf 进行导入。
        /// </summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task ImportAllAsync(CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_scriptsRoot))
            {
                return;
            }

            var pdfFiles = Directory.GetFiles(_scriptsRoot, "*.pdf", SearchOption.TopDirectoryOnly);
            foreach (var pdfPath in pdfFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ImportSingleIfNeededAsync(pdfPath, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 对单个 pdf 执行导入（如果 json 已存在则跳过）。
        /// </summary>
        /// <param name="pdfPath">pdf 文件路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>成功导入则返回 ScriptEpisode，否则返回 null。</returns>
        public async Task<ScriptEpisode?> ImportSingleIfNeededAsync(
            string pdfPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            {
                return null;
            }

            // 从文件名里提取 SxxExx，例如 小谢尔顿-S01E01 -> S01E01
            var episodeCode = TryGetEpisodeCodeFromFileName(Path.GetFileNameWithoutExtension(pdfPath));
            if (string.IsNullOrWhiteSpace(episodeCode))
            {
                // 文件名里没有 SxxExx，暂时跳过
                return null;
            }

            var jsonPath = GetJsonPathByEpisodeCode(episodeCode);
            if (File.Exists(jsonPath))
            {
                // 已经有 json 了，不重复导入
                return null;
            }

            // 使用 PdfPig 抽取 pdf 文本
            string rawText = await ExtractTextFromPdfAsync(pdfPath, cancellationToken)
                .ConfigureAwait(false);

            var episode = new ScriptEpisode
            {
                EpisodeCode = episodeCode,
                Title = Path.GetFileNameWithoutExtension(pdfPath),
                RawText = rawText,
                Segments = new()
                // 这里可以后续人工或工具再填充 Segments
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            await using var fs = File.Create(jsonPath);
            await JsonSerializer.SerializeAsync(fs, episode, options, cancellationToken)
                .ConfigureAwait(false);

            return episode;
        }

        /// <summary>
        /// 根据剧集代码获取对应 json 路径（例如 S01E01.json）。
        /// </summary>
        public string GetJsonPathByEpisodeCode(string episodeCode)
        {
            var safeCode = episodeCode.Trim().ToUpperInvariant();
            return Path.Combine(_scriptsRoot, $"{safeCode}.json");
        }

        /// <summary>
        /// 从文件名中提取 SxxExx 剧集代码（不区分大小写）。
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
        /// 使用 PdfPig 从 pdf 中抽取纯文本。
        /// </summary>
        /// <param name="pdfPath">pdf 路径。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        private static Task<string> ExtractTextFromPdfAsync(
            string pdfPath,
            CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();

            using (var stream = File.OpenRead(pdfPath))
            using (var document = PdfDocument.Open(stream))
            {
                foreach (var page in document.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 使用内容顺序文本抽取器，尽量按阅读顺序拿文本
                    string pageText = ContentOrderTextExtractor.GetText(page);
                    sb.AppendLine(pageText);
                }
            }

            return Task.FromResult(sb.ToString());
        }
    }
}