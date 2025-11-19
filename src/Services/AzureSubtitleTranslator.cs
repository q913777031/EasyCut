#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace EasyCut.Services
{
    /// <summary>
    /// 基于 Azure AI Translator 的字幕翻译实现（英 → 简体中文）。
    /// </summary>
    public sealed class AzureSubtitleTranslator : ISubtitleTranslator
    {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _region;
        private readonly string _subscriptionKey;

        /// <summary>
        /// 构造函数。
        /// 建议通过 DI 注入 HttpClient，并使用环境变量或配置文件提供密钥信息。
        /// </summary>
        public AzureSubtitleTranslator(HttpClient httpClient)
        {
            _httpClient = httpClient;

            // 默认公共云端点，也可以从配置读取，例如：https://api.cognitive.microsofttranslator.com
            _endpoint = Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_ENDPOINT")
                       ?? "https://api.cognitive.microsofttranslator.com";

            _region = Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_REGION")
                       ?? throw new InvalidOperationException(
                           "未配置 AZURE_TRANSLATOR_REGION 环境变量（Azure Translator 区域）。");

            _subscriptionKey = Environment.GetEnvironmentVariable("AZURE_TRANSLATOR_KEY")
                       ?? throw new InvalidOperationException(
                           "未配置 AZURE_TRANSLATOR_KEY 环境变量（Azure Translator 订阅密钥）。");
        }

        /// <summary>
        /// 英文 → 简体中文翻译。
        /// </summary>
        public async Task<string> TranslateEnToZhAsync(
            string english,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(english))
            {
                return string.Empty;
            }

            // Azure Translator REST API:
            // POST {endpoint}/translate?api-version=3.0&from=en&to=zh-Hans
            string url = $"{_endpoint.TrimEnd('/')}/translate" +
                         "?api-version=3.0&from=en&to=zh-Hans";

            // 请求体格式：[{ "Text": "hello" }]
            var body = new[]
            {
                new AzureTranslateRequestItem { Text = english }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Add("Ocp-Apim-Subscription-Key", _subscriptionKey);
            request.Headers.Add("Ocp-Apim-Subscription-Region", _region);

            using var response = await _httpClient.SendAsync(request, cancellationToken)
                                                  .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = await JsonSerializer.DeserializeAsync<
                    List<AzureTranslateResponseItem>>(stream, options, cancellationToken)
                .ConfigureAwait(false);

            string? zh = result?
                .FirstOrDefault()?
                .Translations?
                .FirstOrDefault()?
                .Text;

            // 如果翻译失败，至少返回英文原文，不至于字幕为空
            return string.IsNullOrWhiteSpace(zh) ? english : zh;
        }

        /// <summary>
        /// 请求 DTO。
        /// </summary>
        private sealed class AzureTranslateRequestItem
        {
            [JsonPropertyName("Text")]
            public string Text { get; set; } = string.Empty;
        }

        /// <summary>
        /// 响应 DTO：顶层数组的单项。
        /// </summary>
        private sealed class AzureTranslateResponseItem
        {
            [JsonPropertyName("translations")]
            public List<AzureTranslation>? Translations { get; set; }
        }

        /// <summary>
        /// 响应 DTO：单条翻译结果。
        /// </summary>
        private sealed class AzureTranslation
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }

            [JsonPropertyName("to")]
            public string? To { get; set; }
        }
    }
}