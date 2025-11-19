#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// 字幕翻译服务（英文 → 中文）。
    /// </summary>
    public interface ISubtitleTranslator
    {
        /// <summary>
        /// 将英文句子翻译为简体中文。
        /// </summary>
        Task<string> TranslateEnToZhAsync(
            string english,
            CancellationToken cancellationToken = default);
    }
}