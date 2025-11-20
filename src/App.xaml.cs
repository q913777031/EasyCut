#nullable enable

using System;
using System.ClientModel;
using System.Net.Http;
using System.Windows;
using EasyCut.Scripting;
using EasyCut.Services;
using EasyCut.ViewModels;
using OpenAI;        // ApiKeyCredential / OpenAIClientOptions 在这里
using OpenAI.Chat;  // ChatClient 在这里
using Prism.Ioc;
using Prism.Unity;

namespace EasyCut
{
    /// <summary>
    /// 应用程序入口。
    /// </summary>
    public partial class App : PrismApplication
    {
        /// <inheritdoc />
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // HttpClient 建议单例
            containerRegistry.RegisterSingleton<HttpClient>();

            // 先注册 OpenAI + 片段选择器
            RegisterOpenAiAndSegmentSelector(containerRegistry);

            // 字幕翻译服务（Azure 实现）
            containerRegistry.RegisterSingleton<ISubtitleTranslator, AzureSubtitleTranslator>();

            // 仓储
            containerRegistry.RegisterSingleton<IVideoTaskRepository, InMemoryVideoTaskRepository>();

            // 视频处理（FFmpeg）
            containerRegistry.RegisterSingleton<IVideoProcessingService, FfmpegVideoProcessingService>();

            // 字幕生成（Whisper.net）
            containerRegistry.RegisterSingleton<ISubtitleGenerator, WhisperSubtitleGenerator>();

            // 任务协调（构造函数里会注入 ILearningSegmentSelector）
            containerRegistry.RegisterSingleton<IVideoTaskCoordinator, VideoTaskCoordinator>();

            // 主界面 VM
            containerRegistry.RegisterSingleton<VideoTaskListViewModel>();
        }

        /// <summary>
        /// 注册 OpenAI 客户端和学习片段选择器。
        /// </summary>
        private static void RegisterOpenAiAndSegmentSelector(IContainerRegistry containerRegistry)
        {
            // ChatClient 单例：走 gptsapi 代理
            containerRegistry.RegisterSingleton<ChatClient>(() =>
            {
                const string model = "gpt-4o-mini";

                // ✅ 推荐：优先从环境变量读取 key
                // 在系统里配置：OPENAI_API_KEY = 你在 gptsapi 复制的 key
                var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

                // 如果你暂时懒得配环境变量，可以直接硬编码一份占位：
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    apiKey = "123"; // TODO: 正式环境不要硬编码
                }

                // 关键：自定义 base_url = https://api.gptsapi.net
                return new ChatClient(
                    model: model,
                    credential: new ApiKeyCredential(apiKey),
                    options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri("https://api.gptsapi.net/v1")
                    });
            });

            // 剧本相关
            containerRegistry.RegisterSingleton<IPdfScriptImportService, PdfScriptImportService>();
            containerRegistry.RegisterSingleton<IScriptEpisodeRepository, JsonScriptEpisodeRepository>();
            // 仓储
            containerRegistry.RegisterSingleton<IVideoTaskRepository, InMemoryVideoTaskRepository>();

            // 视频处理
            containerRegistry.RegisterSingleton<IVideoProcessingService, FfmpegVideoProcessingService>();

            // 规则版选择器：具体类型注册（给 OpenAI 选择器做兜底用）
            containerRegistry.RegisterSingleton<RuleBasedLearningSegmentSelector>();

            // OpenAI 驱动的选择器，对外暴露为 ILearningSegmentSelector
            containerRegistry.RegisterSingleton<ILearningSegmentSelector, OpenAiLearningSegmentSelector>();
        }

        /// <inheritdoc />
        protected override Window CreateShell()
        {
            return Container.Resolve<ShellWindow>();
        }
    }
}