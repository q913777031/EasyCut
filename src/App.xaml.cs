#nullable enable

using System.Windows;
using Prism.Ioc;
using Prism.Unity;

namespace EasyCut
{
    public partial class App : PrismApplication
    {
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 仓储
            containerRegistry.RegisterSingleton<Services.IVideoTaskRepository, Services.InMemoryVideoTaskRepository>();

            // 视频处理服务（FFmpeg）
            containerRegistry.RegisterSingleton<Services.IVideoProcessingService, Services.FfmpegVideoProcessingService>();

            // 任务协调服务
            containerRegistry.RegisterSingleton<Services.IVideoTaskCoordinator, Services.VideoTaskCoordinator>();

            // 视图模型
            containerRegistry.RegisterSingleton<ViewModels.VideoTaskListViewModel>();
        }

        protected override Window CreateShell()
        {
            return Container.Resolve<ShellWindow>();
        }
    }
}