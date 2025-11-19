#nullable enable

using System.Net.Http;
using System.Windows;
using EasyCut.Services;
using EasyCut.ViewModels;
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

            // 字幕翻译服务（Azure 实现）
            containerRegistry.RegisterSingleton<ISubtitleTranslator, AzureSubtitleTranslator>();

            // 仓储
            containerRegistry.RegisterSingleton<IVideoTaskRepository, InMemoryVideoTaskRepository>();

            // 视频处理（FFmpeg）
            containerRegistry.RegisterSingleton<IVideoProcessingService, FfmpegVideoProcessingService>();

            // 字幕生成（Whisper.net）
            containerRegistry.RegisterSingleton<ISubtitleGenerator, WhisperSubtitleGenerator>();

            // 任务协调
            containerRegistry.RegisterSingleton<IVideoTaskCoordinator, VideoTaskCoordinator>();

            // 主界面 VM
            containerRegistry.RegisterSingleton<VideoTaskListViewModel>();
        }

        /// <inheritdoc />
        protected override Window CreateShell()
        {
            return Container.Resolve<ShellWindow>();
        }
    }
}