#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;
using Whisper.net.Ggml;

namespace EasyCut.Services
{
    /// <summary>
    /// 基于 Whisper.net 的字幕生成器实现。
    /// </summary>
    public sealed class WhisperSubtitleGenerator : ISubtitleGenerator
    {
        private readonly string _modelFilePath;

        public WhisperSubtitleGenerator()
        {
            _modelFilePath = Path.Combine(
                AppContext.BaseDirectory,
                "Models",
                "ggml-base.en.bin");
        }

        /// <summary>
        /// 将 wav 音频直接转成英文字幕条目（不落地文件）。
        /// </summary>
        public async Task<IReadOnlyList<SrtEntry>> TranscribeAsync(
      string wavFilePath,
      CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wavFilePath))
                throw new ArgumentException("音频路径不能为空。", nameof(wavFilePath));

            if (!File.Exists(wavFilePath))
                throw new FileNotFoundException("找不到音频文件。", wavFilePath);

            await EnsureModelExistsAsync(cancellationToken).ConfigureAwait(false);

            var entries = new List<SrtEntry>();

            using var factory = WhisperFactory.FromPath(_modelFilePath);

            var builder = factory.CreateBuilder()
                .WithLanguage("en");    // 只识别英文，不做翻译

            using var processor = builder.Build();

            using var fileStream = File.OpenRead(wavFilePath);

            await foreach (var result in processor
                               .ProcessAsync(fileStream)
                               .WithCancellation(cancellationToken))
            {
                string text = result.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var entry = new SrtEntry
                {
                    Index = entries.Count + 1,
                    Start = result.Start,
                    End = result.End
                };
                entry.Lines.Add(text);
                entries.Add(entry);
            }

            return entries;
        }

        /// <summary>
        /// 生成英文字幕和中英字幕 SRT 文件。
        /// </summary>
        public async Task<(string englishSrtPath, string bilingualSrtPath)> GenerateEnglishAndBilingualSrtAsync(
            string wavFilePath,
            string outputDirectory,
            string? baseFileName = null,
            Func<string, Task<string>>? translateAsync = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
                throw new ArgumentException("输出目录不能为空。", nameof(outputDirectory));

            Directory.CreateDirectory(outputDirectory);

            var englishEntries = await TranscribeAsync(wavFilePath, cancellationToken)
                .ConfigureAwait(false);

            if (englishEntries.Count == 0)
                throw new InvalidOperationException("未识别到任何语音内容，无法生成字幕。");

            string safeBaseName = string.IsNullOrWhiteSpace(baseFileName)
                ? Path.GetFileNameWithoutExtension(wavFilePath)
                : baseFileName;

            string englishPath = Path.Combine(outputDirectory, $"{safeBaseName}.en.srt");
            string bilingualPath = Path.Combine(outputDirectory, $"{safeBaseName}.en-zh.srt");

            // 英文字幕：直接写
            await SrtHelper.WriteAsync(englishPath, englishEntries)
                .ConfigureAwait(false);

            IReadOnlyList<SrtEntry> bilingualEntries;

            if (translateAsync is null)
            {
                // 没有翻译函数时，【不再重复两行英文】，
                // 直接用英文字幕（单行英文）作为“中英字幕”占位。
                bilingualEntries = englishEntries;
            }
            else
            {
                // 有翻译函数时，生成“英文 + 中文”两行
                bilingualEntries = await ToBilingualAsync(
                        englishEntries,
                        translateAsync,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await SrtHelper.WriteAsync(bilingualPath, bilingualEntries)
                .ConfigureAwait(false);

            return (englishPath, bilingualPath);
        }

        /// <summary>
        /// 将英文字幕转为中英双语（英文 + 中文两行）。
        /// </summary>
        private static async Task<IReadOnlyList<SrtEntry>> ToBilingualAsync(
            IReadOnlyList<SrtEntry> englishEntries,
            Func<string, Task<string>> translateAsync,
            CancellationToken cancellationToken)
        {
            var result = new List<SrtEntry>(englishEntries.Count);

            foreach (var e in englishEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string english = string.Join(" ", e.Lines).Trim();
                if (string.IsNullOrWhiteSpace(english))
                    continue;

                string chinese = await translateAsync(english).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(chinese))
                    chinese = english; // 翻译失败时退回英文

                var entry = new SrtEntry
                {
                    Index = result.Count + 1,
                    Start = e.Start,
                    End = e.End
                };

                entry.Lines.Add(english);
                entry.Lines.Add(chinese);

                result.Add(entry);
            }

            return result;
        }

        /// <summary>
        /// 确保 ggml 模型存在，不存在则自动下载。
        /// </summary>
        private async Task EnsureModelExistsAsync(CancellationToken cancellationToken)
        {
            string? dir = Path.GetDirectoryName(_modelFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            bool needDownload = true;

            if (File.Exists(_modelFilePath))
            {
                var fi = new FileInfo(_modelFilePath);
                if (fi.Length > 50_000_000)
                {
                    needDownload = false;
                }
                else
                {
                    try { File.Delete(_modelFilePath); } catch { }
                }
            }

            if (!needDownload)
                return;

            using var modelStream = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(GgmlType.BaseEn)
                .ConfigureAwait(false);

            using var fileWriter = File.OpenWrite(_modelFilePath);
            await modelStream.CopyToAsync(fileWriter, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}