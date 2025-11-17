using Microsoft.Win32;
using Prism.Commands;
using Prism.Mvvm;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using EasyCut.Services;

namespace EasyCut.ViewModels
{
    /// <summary>
    /// 主窗口视图模型，负责界面数据和命令处理。
    /// </summary>
    public class MainWindowViewModel : BindableBase
    {
        /// <summary>
        /// ffmpeg 服务实例。
        /// </summary>
        private readonly FfmpegService _ffmpegService;

        /// <summary>
        /// 视频文件路径。
        /// </summary>
        private string _videoPath = string.Empty;

        /// <summary>
        /// 英文字幕文件路径。
        /// </summary>
        private string _englishSubtitlePath = string.Empty;

        /// <summary>
        /// 中英字幕文件路径。
        /// </summary>
        private string _bilingualSubtitlePath = string.Empty;

        /// <summary>
        /// 开始时间文本。
        /// </summary>
        private string _startText = "00:00:00";

        /// <summary>
        /// 结束时间文本。
        /// </summary>
        private string _endText = "00:00:10";

        /// <summary>
        /// 输出文件路径。
        /// </summary>
        private string _outputPath = string.Empty;

        /// <summary>
        /// 日志文本。
        /// </summary>
        private string _logText = string.Empty;

        /// <summary>
        /// 是否正在处理。
        /// </summary>
        private bool _isBusy;

        /// <summary>
        /// 获取或设置视频文件路径。
        /// </summary>
        public string VideoPath
        {
            get { return _videoPath; }
            set
            {
                if (SetProperty(ref _videoPath, value))
                {
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取或设置英文字幕文件路径。
        /// </summary>
        public string EnglishSubtitlePath
        {
            get { return _englishSubtitlePath; }
            set
            {
                if (SetProperty(ref _englishSubtitlePath, value))
                {
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取或设置中英字幕文件路径。
        /// </summary>
        public string BilingualSubtitlePath
        {
            get { return _bilingualSubtitlePath; }
            set
            {
                if (SetProperty(ref _bilingualSubtitlePath, value))
                {
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取或设置开始时间文本。
        /// </summary>
        public string StartText
        {
            get { return _startText; }
            set
            {
                if (SetProperty(ref _startText, value))
                {
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取或设置结束时间文本。
        /// </summary>
        public string EndText
        {
            get { return _endText; }
            set
            {
                if (SetProperty(ref _endText, value))
                {
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取或设置输出文件路径。
        /// </summary>
        public string OutputPath
        {
            get { return _outputPath; }
            set
            {
                if (SetProperty(ref _outputPath, value))
                {
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取或设置日志文本。
        /// </summary>
        public string LogText
        {
            get { return _logText; }
            set { SetProperty(ref _logText, value); }
        }

        /// <summary>
        /// 获取或设置是否正在处理。
        /// </summary>
        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaisePropertyChanged(nameof(IsNotBusy));
                    GenerateCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 获取一个值，表示当前是否空闲。
        /// </summary>
        public bool IsNotBusy
        {
            get { return !IsBusy; }
        }

        /// <summary>
        /// 浏览视频文件命令。
        /// </summary>
        public DelegateCommand BrowseVideoCommand { get; private set; }

        /// <summary>
        /// 浏览英文字幕文件命令。
        /// </summary>
        public DelegateCommand BrowseEnglishSubtitleCommand { get; private set; }

        /// <summary>
        /// 浏览中英字幕文件命令。
        /// </summary>
        public DelegateCommand BrowseBilingualSubtitleCommand { get; private set; }

        /// <summary>
        /// 浏览输出文件命令。
        /// </summary>
        public DelegateCommand BrowseOutputCommand { get; private set; }

        /// <summary>
        /// 生成学习视频命令。
        /// </summary>
        public DelegateCommand GenerateCommand { get; private set; }

        /// <summary>
        /// 初始化主窗口视图模型。
        /// </summary>
        /// <param name="ffmpegService">ffmpeg 服务实例。</param>
        public MainWindowViewModel(FfmpegService ffmpegService)
        {
            _ffmpegService = ffmpegService;

            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            _outputPath = Path.Combine(desktop, "learning_video.mp4");

            BrowseVideoCommand = new DelegateCommand(BrowseVideo);
            BrowseEnglishSubtitleCommand = new DelegateCommand(BrowseEnglishSubtitle);
            BrowseBilingualSubtitleCommand = new DelegateCommand(BrowseBilingualSubtitle);
            BrowseOutputCommand = new DelegateCommand(BrowseOutput);
            GenerateCommand = new DelegateCommand(ExecuteGenerate, CanGenerate);
        }

        /// <summary>
        /// 判断是否可以执行生成命令。
        /// </summary>
        /// <returns>如果可以执行则为 true。</returns>
        private bool CanGenerate()
        {
            return !IsBusy
                && !string.IsNullOrWhiteSpace(VideoPath)
                && !string.IsNullOrWhiteSpace(EnglishSubtitlePath)
                && !string.IsNullOrWhiteSpace(BilingualSubtitlePath)
                && !string.IsNullOrWhiteSpace(StartText)
                && !string.IsNullOrWhiteSpace(EndText)
                && !string.IsNullOrWhiteSpace(OutputPath);
        }

        /// <summary>
        /// 执行浏览视频文件操作。
        /// </summary>
        private void BrowseVideo()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "视频文件|*.mp4;*.mkv;*.avi;*.mov|所有文件|*.*";
            if (dialog.ShowDialog() == true)
            {
                VideoPath = dialog.FileName;
                AppendLog("选择视频文件：" + VideoPath);
            }
        }

        /// <summary>
        /// 执行浏览英文字幕文件操作。
        /// </summary>
        private void BrowseEnglishSubtitle()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "字幕文件|*.srt;*.ass;*.ssa|所有文件|*.*";
            if (dialog.ShowDialog() == true)
            {
                EnglishSubtitlePath = dialog.FileName;
                AppendLog("选择英文字幕文件：" + EnglishSubtitlePath);
            }
        }

        /// <summary>
        /// 执行浏览中英字幕文件操作。
        /// </summary>
        private void BrowseBilingualSubtitle()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "字幕文件|*.srt;*.ass;*.ssa|所有文件|*.*";
            if (dialog.ShowDialog() == true)
            {
                BilingualSubtitlePath = dialog.FileName;
                AppendLog("选择中英字幕文件：" + BilingualSubtitlePath);
            }
        }

        /// <summary>
        /// 执行浏览输出文件路径操作。
        /// </summary>
        private void BrowseOutput()
        {
            SaveFileDialog dialog = new SaveFileDialog();
            dialog.Filter = "视频文件|*.mp4";
            dialog.FileName = Path.GetFileName(OutputPath);
            if (dialog.ShowDialog() == true)
            {
                OutputPath = dialog.FileName;
                AppendLog("选择输出文件：" + OutputPath);
            }
        }

        /// <summary>
        /// 追加一行日志信息。
        /// </summary>
        /// <param name="message">日志内容。</param>
        private void AppendLog(string message)
        {
            LogText = LogText + "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine;
        }

        /// <summary>
        /// 执行生成命令的入口。
        /// </summary>
        private async void ExecuteGenerate()
        {
            await GenerateAsync();
        }

        /// <summary>
        /// 执行生成学习视频的完整流程。
        /// </summary>
        /// <returns>表示异步操作的任务。</returns>
        private async Task GenerateAsync()
        {
            if (!File.Exists(VideoPath))
            {
                AppendLog("视频文件不存在。");
                MessageBox.Show("视频文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!File.Exists(EnglishSubtitlePath))
            {
                AppendLog("英文字幕文件不存在。");
                MessageBox.Show("英文字幕文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!File.Exists(BilingualSubtitlePath))
            {
                AppendLog("中英字幕文件不存在。");
                MessageBox.Show("中英字幕文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            TimeSpan start;
            if (!TimeSpan.TryParse(StartText, out start))
            {
                AppendLog("开始时间格式不正确，应为 HH:MM:SS。");
                MessageBox.Show("开始时间格式不正确，应为 HH:MM:SS。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            TimeSpan end;
            if (!TimeSpan.TryParse(EndText, out end))
            {
                AppendLog("结束时间格式不正确，应为 HH:MM:SS。");
                MessageBox.Show("结束时间格式不正确，应为 HH:MM:SS。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (end <= start)
            {
                AppendLog("结束时间必须大于开始时间。");
                MessageBox.Show("结束时间必须大于开始时间。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                IsBusy = true;
                AppendLog("开始生成学习视频...");

                await _ffmpegService.GenerateLearningVideoAsync(
                    VideoPath,
                    EnglishSubtitlePath,
                    BilingualSubtitlePath,
                    start,
                    end,
                    OutputPath,
                    AppendLog);

                AppendLog("生成完成。");
                MessageBox.Show("生成完成！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog("生成失败：" + ex.Message);
                MessageBox.Show("生成失败：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}