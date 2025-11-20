#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EasyCut.Scripting;
using UglyToad.PdfPig;

namespace EasyCut.Services
{
    /// <summary>
    /// 基于 PdfPig 的 PDF 剧本导入服务实现：
    /// - 从 PDF 抽取台词和生词；
    /// - 生成 ScriptEpisode（Lines + 预设 Segments）；
    /// - 保存为 json：ScriptsEpisodes\S01E01.json。
    /// </summary>
    public sealed class PdfScriptImportService : IPdfScriptImportService
    {
        /// <summary>
        /// 剧本 JSON / PDF 存储根目录（和你之前约定的 ScriptsEpisodes 一致）。
        /// </summary>
        private readonly string _scriptsRoot;

        /// <summary>
        /// 构造函数。
        /// </summary>
        public PdfScriptImportService()
        {
            _scriptsRoot = Path.Combine(AppContext.BaseDirectory, "ScriptsEpisodes");
            Directory.CreateDirectory(_scriptsRoot);
        }

        /// <inheritdoc />
        public async Task<ScriptEpisode> ImportAsync(
            string pdfPath,
            string episodeId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(pdfPath))
            {
                throw new ArgumentException("PDF 路径不能为空。", nameof(pdfPath));
            }

            if (!File.Exists(pdfPath))
            {
                throw new FileNotFoundException("找不到 PDF 文件。", pdfPath);
            }

            var episode = new ScriptEpisode
            {
                EpisodeId = episodeId,                                   // S01E01
                EpisodeCode = episodeId,
                SourcePdfFileName = Path.GetFileName(pdfPath),
                Title = Path.GetFileNameWithoutExtension(pdfPath)
            };

            var allTextLines = new List<(int PageNumber, string Text)>();

            using (var document = PdfDocument.Open(pdfPath))
            {
                foreach (var page in document.GetPages())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string text = page.Text;
                    var lines = text.Split(
                        new[] { "\r\n", "\n" },
                        StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length == 0)
                        {
                            continue;
                        }

                        allTextLines.Add((page.Number, trimmed));
                    }
                }
            }

            // RawText 方便后续调试或进一步处理
            episode.RawText = string.Join(Environment.NewLine, allTextLines.Select(x => x.Text));

            var scriptLines = new List<ScriptLine>();
            var pageKeywords = new Dictionary<int, List<string>>();

            // 例：This is a sentence. 这是句子。[01:23]
            var dialogueRegex = new Regex(
                @"^(?<en>.+?)\s+(?<zh>[\u4e00-\u9fa5，。？！：；、“”‘’…·《》〈〉]+)\[(?<mm>\d{2}):(?<ss>\d{2})]$",
                RegexOptions.Compiled);

            // 例：word: explanation
            var vocabRegex = new Regex(
                @"^(?<word>[A-Za-z][A-Za-z\-']*)\s*:\s*(?<exp>.+)$",
                RegexOptions.Compiled);

            foreach (var (pageNumber, textLine) in allTextLines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) 先尝试匹配“台词 + 中文 + [mm:ss]”
                var md = dialogueRegex.Match(textLine);
                if (md.Success)
                {
                    string en = md.Groups["en"].Value.Trim();
                    string zh = md.Groups["zh"].Value.Trim();
                    string mm = md.Groups["mm"].Value;
                    string ss = md.Groups["ss"].Value;

                    double startSeconds = ParseTimestampToSeconds(mm, ss);

                    var line = new ScriptLine
                    {
                        StartSeconds = startSeconds,
                        EndSeconds = startSeconds + 2, // 先给个默认，后面再统一修正
                        English = en,
                        Chinese = zh
                    };
                    scriptLines.Add(line);
                    continue;
                }

                // 2) 再尝试匹配“生词: 解释”
                var mv = vocabRegex.Match(textLine);
                if (mv.Success)
                {
                    string word = mv.Groups["word"].Value.Trim();
                    if (!pageKeywords.TryGetValue(pageNumber, out var list))
                    {
                        list = new List<string>();
                        pageKeywords[pageNumber] = list;
                    }

                    if (!string.IsNullOrWhiteSpace(word) &&
                        !list.Contains(word, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(word);
                    }
                }
            }

            // 按时间排序并修正 EndSeconds（当前 ≈ 下一句开始 - 0.2s）
            scriptLines = scriptLines.OrderBy(l => l.StartSeconds).ToList();
            for (int i = 0; i < scriptLines.Count; i++)
            {
                var current = scriptLines[i];
                if (i < scriptLines.Count - 1)
                {
                    var next = scriptLines[i + 1];
                    current.EndSeconds = Math.Max(
                        current.StartSeconds + 0.5,
                        next.StartSeconds - 0.2);
                }
                else
                {
                    current.EndSeconds = current.StartSeconds + 2;
                }
            }

            episode.Lines.AddRange(scriptLines);

            // 每页生成一个“预设学习片段”（简单规则，后面你可以再细化）
            var pageNumbers = allTextLines
                .Select(x => x.PageNumber)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            int segIndex = 1;
            foreach (var pageNumber in pageNumbers)
            {
                var linesOnPage = scriptLines
                    .Where(l => BelongsToPage(l.StartSeconds, pageNumber, pageNumbers))
                    .OrderBy(l => l.StartSeconds)
                    .ToList();

                if (linesOnPage.Count == 0)
                    continue;

                double start = linesOnPage.First().StartSeconds;
                double end = linesOnPage.Last().EndSeconds;

                pageKeywords.TryGetValue(pageNumber, out var kws);
                kws ??= new List<string>();

                var seg = new LearningSegmentConfig
                {
                    Id = $"{episodeId}-{segIndex:D3}",
                    Title = $"第 {pageNumber} 页片段",
                    StartSeconds = start,
                    EndSeconds = end,
                    Keywords = new List<string>(kws),
                    PreviewText = string.Join(" / ",
                        linesOnPage.Take(3).Select(l => l.English))
                };

                episode.Segments.Add(seg);
                segIndex++;
            }

            return episode;
        }

        /// <inheritdoc />
        public async Task SaveEpisodeAsync(
            ScriptEpisode episode,
            CancellationToken cancellationToken = default)
        {
            if (episode is null)
                throw new ArgumentNullException(nameof(episode));

            Directory.CreateDirectory(_scriptsRoot);

            string jsonPath = Path.Combine(
                _scriptsRoot,
                $"{episode.EpisodeId}.json"); // 比如 S01E01.json

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            await using var stream = File.Create(jsonPath);
            await JsonSerializer.SerializeAsync(stream, episode, options, cancellationToken)
                .ConfigureAwait(false);
        }

        private static double ParseTimestampToSeconds(string mm, string ss)
        {
            if (!int.TryParse(mm, out int m)) m = 0;
            if (!int.TryParse(ss, out int s)) s = 0;
            return m * 60 + s;
        }

        /// <summary>
        /// 简单根据页号划分片段（目前占位，默认全部 true）。
        /// 以后你可以根据 StartSeconds 做更精细的映射。
        /// </summary>
        private static bool BelongsToPage(
            double startSeconds,
            int pageNumber,
            IReadOnlyList<int> allPages)
        {
            // TODO: 根据你实际的时间 / 页号规则来划分。
            return true;
        }
    }
}