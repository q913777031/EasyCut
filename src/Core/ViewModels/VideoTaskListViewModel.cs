#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using EasyCut.Models;
using EasyCut.Services;
using Prism.Commands;
using Prism.Mvvm;

namespace EasyCut.ViewModels
{
    /// <summary>
    /// 视频任务列表视图模型
    /// </summary>
    public class VideoTaskListViewModel : BindableBase
    {
        private readonly IVideoTaskCoordinator _coordinator;

        /// <summary>
        /// 任务集合
        /// </summary>
        public ObservableCollection<VideoTask> Tasks { get; } = new();

        private VideoTask? _selectedTask;

        /// <summary>
        /// 当前选中的任务
        /// </summary>
        public VideoTask? SelectedTask
        {
            get => _selectedTask;
            set => SetProperty(ref _selectedTask, value);
        }

        /// <summary>
        /// 新建任务命令
        /// </summary>
        public DelegateCommand NewTaskCommand { get; }

        /// <summary>
        /// 刷新命令
        /// </summary>
        public DelegateCommand RefreshCommand { get; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public VideoTaskListViewModel(IVideoTaskCoordinator coordinator)
        {
            _coordinator = coordinator;
            NewTaskCommand = new DelegateCommand(OnNewTask);
            RefreshCommand = new DelegateCommand(OnRefresh);
            OpenOutputFolderCommand = new DelegateCommand(OnOpenOutputFolder, CanOpenOutputFolder)
    .ObservesProperty(() => SelectedTask);
        }

        private async void OnNewTask()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "视频文件|*.mp4;*.mkv;*.mov;*.avi|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true)
                return;

            string videoPath = dlg.FileName;
            string outputDir = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(videoPath)!,
                "EasyCutOutput");

            System.IO.Directory.CreateDirectory(outputDir);

            var task = await _coordinator.CreateAndRunTaskAsync(videoPath, outputDir);
            Tasks.Add(task);
            SelectedTask = task;
            if (task.Status == VideoTaskStatus.Failed)
            {
                System.Windows.MessageBox.Show(
                    task.ErrorMessage ?? "未知错误",
                    "剪辑失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async void OnRefresh()
        {
            for (int i = 0; i < Tasks.Count; i++)
            {
                var current = Tasks[i];
                var latest = await _coordinator.GetTaskAsync(current.Id);
                if (latest is not null)
                {
                    current.Status = latest.Status;
                    current.Progress = latest.Progress;
                    current.ErrorMessage = latest.ErrorMessage;
                    current.UpdatedTime = latest.UpdatedTime;
                }
            }
        }

        public DelegateCommand OpenOutputFolderCommand { get; }

        private bool CanOpenOutputFolder()
        {
            return SelectedTask != null
                   && !string.IsNullOrWhiteSpace(SelectedTask.OutputDirectory)
                   && System.IO.Directory.Exists(SelectedTask.OutputDirectory);
        }

        private void OnOpenOutputFolder()
        {
            if (SelectedTask == null) return;

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