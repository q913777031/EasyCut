#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// 字幕生成器接口（根据音频自动生成英文 / 中英字幕）。
    /// </summary>
    public interface ISubtitleGenerator
    {
        /// <summary>
        /// 将音频转成英文字幕条目集合（不落地文件）。
        /// </summary>
        Task<IReadOnlyList<SrtEntry>> TranscribeAsync(
            string wavFilePath,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 生成英文字幕和中英字幕 SRT 文件，返回两个文件路径。
        /// </summary>
        /// <param name="wavFilePath">16k 单声道 wav 文件路径。</param>
        /// <param name="outputDirectory">输出目录。</param>
        /// <param name="baseFileName">基础文件名（不含扩展名），为空则使用音频文件名。</param>
        /// <param name="translateAsync">英文 → 中文翻译函数，为空则中英字幕第二行暂用英文占位。</param>
        Task<(string englishSrtPath, string bilingualSrtPath)> GenerateEnglishAndBilingualSrtAsync(
            string wavFilePath,
            string outputDirectory,
            string? baseFileName = null,
            Func<string, Task<string>>? translateAsync = null,
            CancellationToken cancellationToken = default);
    }
}