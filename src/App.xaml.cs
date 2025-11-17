using System.ComponentModel;
using System.Windows;
using EasyCut.Shell;
using EasyCut.Shell.Views;
using EasyCut.Views;
using Prism.Ioc;
using Prism.Unity;

namespace EasyCut
{
    /// <summary>
    /// 应用程序入口类，负责 Prism 框架初始化。
    /// </summary>
    public partial class App : PrismApplication
    {
        /// <summary>
        /// 创建应用程序主窗口。
        /// </summary>
        /// <returns>主窗口对象。</returns>
        protected override Window CreateShell()
        {
            return Container.Resolve<ShellWindow>();
        }

        /// <summary>
        /// 注册依赖注入所需的类型和服务。
        /// </summary>
        /// <param name="containerRegistry">容器注册器。</param>
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterForNavigation<VideoTaskListView>("VideoTaskList");
            containerRegistry.RegisterSingleton<Services.IVideoTaskRepository, Services.InMemoryVideoTaskRepository>();
            containerRegistry.RegisterSingleton<Services.IVideoTaskCoordinator, Services.VideoTaskCoordinator>();
            containerRegistry.RegisterSingleton<ViewModels.VideoTaskListViewModel>();
        }
    }
}