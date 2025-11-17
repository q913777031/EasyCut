using Prism.Ioc;
using Prism.Unity;
using System.Windows;
using EasyCut.Views;
using System.ComponentModel;

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
            return Container.Resolve<MainWindow>();
        }

        /// <summary>
        /// 注册依赖注入所需的类型和服务。
        /// </summary>
        /// <param name="containerRegistry">容器注册器。</param>
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            containerRegistry.RegisterSingleton<Services.FfmpegService>();
        }
    }
}