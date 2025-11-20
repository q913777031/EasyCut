#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using EasyCut.Models;
using EasyCut.Services;
using Prism.Commands;
using Prism.Mvvm;
using EasyCut.Views;
using Microsoft.Win32;
using System.IO;

namespace EasyCut.ViewModels
{
    /// <summary>
    /// 视频任务列表视图模型。
    /// </summary>
    public sealed class VideoTaskListViewModel : BindableBase
    {
        private readonly IVideoTaskCoordinator _coordinator;

        /// <summary>
        /// 任务集合。
        /// </summary>
        public ObservableCollection<VideoTask> Tasks { get; } = new();

        private VideoTask? _selectedTask;

        /// <summary>
        /// 当前选中的任务。
        /// </summary>
        public VideoTask? SelectedTask
        {
            get => _selectedTask;
            set => SetProperty(ref _selectedTask, value);
        }

        /// <summary>
        /// 新建任务命令。
        /// </summary>
        public DelegateCommand NewTaskCommand { get; }

        /// <summary>
        /// 刷新命令。
        /// </summary>
        public DelegateCommand RefreshCommand { get; }

        /// <summary>
        /// 打开输出目录命令。
        /// </summary>
        public DelegateCommand OpenOutputFolderCommand { get; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public VideoTaskListViewModel(IVideoTaskCoordinator coordinator)
        {
            _coordinator = coordinator;

            NewTaskCommand = new DelegateCommand(OnNewTask);
            RefreshCommand = new DelegateCommand(OnRefresh);

            OpenOutputFolderCommand = new DelegateCommand(OnOpenOutputFolder, CanOpenOutputFolder)
                .ObservesProperty(() => SelectedTask);
        }

        /// <summary>
        /// 新建任务：选择文件 → 创建任务 → 加入列表 → 后台执行。
        /// </summary>
        private async void OnNewTask()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.mkv;*.mov;*.avi|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true)
                return;

            string videoPath = dlg.FileName;
            string outputDir = Path.Combine(
                Path.GetDirectoryName(videoPath)!,
                "EasyCutOutput");

            Directory.CreateDirectory(outputDir);

            // ① 先让用户可视化选择片段
            if (!SegmentSelectionDialog.TrySelectSegment(videoPath, out var clipStart, out var clipEnd))
            {
                // 用户点击了“取消”
                return;
            }

            // ② 创建任务，并把手工片段写到任务里
            var task = await _coordinator.CreateTaskAsync(videoPath, outputDir);
            task.ClipStartSeconds = clipStart;
            task.ClipEndSeconds = clipEnd;

            Tasks.Add(task);
            SelectedTask = task;

            // ③ 后台执行任务（流程里会优先使用 ClipStart/End）
            _ = _coordinator.RunTaskAsync(task.Id)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted || task.Status == VideoTaskStatus.Failed)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(
                                task.ErrorMessage ?? t.Exception?.GetBaseException().Message ?? "未知错误",
                                "剪辑失败",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        });
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// 手动刷新任务状态（从协调器重新获取最新信息）。
        /// </summary>
        private async void OnRefresh()
        {
            for (var i = 0; i < Tasks.Count; i++)
            {
                var current = Tasks[i];
                var latest = await _coordinator.GetTaskAsync(current.Id);
                if (latest is not null)
                {
                    current.Status = latest.Status;
                    current.Progress = latest.Progress;
                    current.ErrorMessage = latest.ErrorMessage;
                    current.UpdatedTime = latest.UpdatedTime;
                    current.OutputFilePath = latest.OutputFilePath;
                    current.Phase = latest.Phase;
                }
            }
        }

        /// <summary>
        /// 是否可以打开输出目录。
        /// </summary>
        private bool CanOpenOutputFolder()
        {
            return SelectedTask != null
                   && !string.IsNullOrWhiteSpace(SelectedTask.OutputDirectory)
                   && System.IO.Directory.Exists(SelectedTask.OutputDirectory);
        }

        /// <summary>
        /// 打开输出目录。
        /// </summary>
        private void OnOpenOutputFolder()
        {
            if (SelectedTask == null)
            {
                return;
            }

            var dir = SelectedTask.OutputDirectory;
            if (System.IO.Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
    }
}