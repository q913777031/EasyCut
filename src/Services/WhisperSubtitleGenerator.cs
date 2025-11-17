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
        /// <summary>
        /// 默认模型类型（Base 英文模型）
        /// </summary>
        private const GgmlType DefaultModelType = GgmlType.BaseEn;

        /// <summary>
        /// 默认模型文件名
        /// </summary>
        private const string DefaultModelFileName = "ggml-base.en.bin";

        /// <summary>
        /// 模型文件完整路径
        /// </summary>
        private readonly string _modelFilePath;

        /// <summary>
        /// 识别语言（"en" 或 "auto"）
        /// </summary>
        private readonly string _language;

        /// <summary>
        /// 构造函数（使用默认 Models 目录、BaseEn 模型、英文）
        /// </summary>
        public WhisperSubtitleGenerator()
            : this(
                  modelDirectory: Path.Combine(AppContext.BaseDirectory, "Models"),
                  modelType: DefaultModelType,
                  modelFileName: DefaultModelFileName,
                  language: "en")
        {
        }

        /// <summary>
        /// 自定义模型目录、类型、文件名和语言的构造函数。
        /// </summary>
        public WhisperSubtitleGenerator(
            string modelDirectory,
            GgmlType modelType,
            string modelFileName,
            string language = "en")
        {
            if (string.IsNullOrWhiteSpace(modelDirectory))
            {
                throw new ArgumentException("模型目录不能为空。", nameof(modelDirectory));
            }

            if (string.IsNullOrWhiteSpace(modelFileName))
            {
                throw new ArgumentException("模型文件名不能为空。", nameof(modelFileName));
            }

            Directory.CreateDirectory(modelDirectory);

            ModelType = modelType;
            _modelFilePath = Path.Combine(modelDirectory, modelFileName);
            _language = string.IsNullOrWhiteSpace(language) ? "auto" : language;
        }

        /// <summary>
        /// 当前模型类型（可用于日志）
        /// </summary>
        public GgmlType ModelType { get; }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SrtEntry>> TranscribeAsync(
            string wavFilePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(wavFilePath))
            {
                throw new ArgumentException("音频路径不能为空。", nameof(wavFilePath));
            }

            if (!File.Exists(wavFilePath))
            {
                throw new FileNotFoundException("找不到音频文件。", wavFilePath);
            }

            await EnsureModelExistsAsync(cancellationToken).ConfigureAwait(false);

            var entries = new List<SrtEntry>();

            using var whisperFactory = WhisperFactory.FromPath(_modelFilePath);

            using var processor = whisperFactory.CreateBuilder()
                .WithLanguage(_language)   // "en" 或 "auto"
                .Build();

            using var fileStream = File.OpenRead(wavFilePath);

            await foreach (var result in processor.ProcessAsync(fileStream).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string text = result.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                // Whisper.net 的 Start / End 为 TimeSpan（新版）
                TimeSpan start = result.Start;
                TimeSpan end = result.End;

                if (end <= start)
                {
                    end = start + TimeSpan.FromMilliseconds(500);
                }

                var entry = new SrtEntry
                {
                    Index = entries.Count + 1,
                    Start = start,
                    End = end
                };
                entry.Lines.Add(text);

                entries.Add(entry);
            }

            return entries;
        }

        /// <inheritdoc />
        public async Task<(string englishSrtPath, string bilingualSrtPath)> GenerateEnglishAndBilingualSrtAsync(
            string wavFilePath,
            string outputDirectory,
            string? baseFileName = null,
            Func<string, Task<string>>? translateAsync = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("输出目录不能为空。", nameof(outputDirectory));
            }

            Directory.CreateDirectory(outputDirectory);

            var entries = await TranscribeAsync(wavFilePath, cancellationToken).ConfigureAwait(false);
            if (entries.Count == 0)
            {
                throw new InvalidOperationException("未识别到任何语音内容，无法生成字幕。");
            }

            string safeBaseName = string.IsNullOrWhiteSpace(baseFileName)
                ? Path.GetFileNameWithoutExtension(wavFilePath)
                : baseFileName;

            string englishPath = Path.Combine(outputDirectory, $"{safeBaseName}.en.srt");
            string bilingualPath = Path.Combine(outputDirectory, $"{safeBaseName}.en-zh.srt");

            // 英文字幕文件
            await SrtHelper.WriteAsync(englishPath, entries).ConfigureAwait(false);

            // 中英字幕文件（第二行中文，先用翻译函数占位）
            var bilingualEntries = new List<SrtEntry>();

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string englishText = string.Join(" ", entry.Lines).Trim();
                if (string.IsNullOrWhiteSpace(englishText))
                {
                    continue;
                }

                string chineseText = await TranslateOrFallbackAsync(
                    englishText,
                    translateAsync,
                    cancellationToken).ConfigureAwait(false);

                var newEntry = new SrtEntry
                {
                    Index = bilingualEntries.Count + 1,
                    Start = entry.Start,
                    End = entry.End
                };
                newEntry.Lines.Add(englishText);
                newEntry.Lines.Add(chineseText);

                bilingualEntries.Add(newEntry);
            }

            if (bilingualEntries.Count == 0)
            {
                await SrtHelper.WriteAsync(bilingualPath, entries).ConfigureAwait(false);
            }
            else
            {
                await SrtHelper.WriteAsync(bilingualPath, bilingualEntries).ConfigureAwait(false);
            }

            return (englishPath, bilingualPath);
        }

        /// <summary>
        /// 确保本地模型文件存在，不存在则从 Hugging Face 自动下载。
        /// </summary>
        private async Task EnsureModelExistsAsync(CancellationToken cancellationToken)
        {
            if (File.Exists(_modelFilePath))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_modelFilePath)!);

            using var modelStream = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(ModelType, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using var fileWriter = File.OpenWrite(_modelFilePath);
            await modelStream.CopyToAsync(fileWriter, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 翻译英文文本为中文；如未提供翻译函数或翻译失败，则回退为英文。
        /// </summary>
        private static async Task<string> TranslateOrFallbackAsync(
            string english,
            Func<string, Task<string>>? translateAsync,
            CancellationToken cancellationToken)
        {
            if (translateAsync is null)
            {
                // 当前先直接返回英文；后续可接入任意 AI / 翻译服务。
                return english;
            }

            try
            {
                string result = await translateAsync(english).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    return result.Trim();
                }
            }
            catch
            {
                // 忽略翻译异常
            }

            return english;
        }
    }
}