#nullable enable

using System.Windows;
using EasyCut.ViewModels;

namespace EasyCut
{
    /// <summary>
    /// 主窗口
    /// </summary>
    public partial class ShellWindow : Window
    {
        /// <summary>
        /// 构造函数，Prism 自动注入视图模型
        /// </summary>
        public ShellWindow(VideoTaskListViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }
    }
}