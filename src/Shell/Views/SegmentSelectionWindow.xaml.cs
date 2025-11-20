#nullable enable

using System;
using System.Windows;
using System.Windows.Threading;

namespace EasyCut.Views
{
    /// <summary>
    /// 片段选择窗口：提供简单的视频预览和开始/结束时间选择。
    /// </summary>
    public partial class SegmentSelectionWindow : Window
    {
        private readonly string _videoPath;
        private readonly DispatcherTimer _timer;

        private bool _isPlaying;
        private bool _isPreviewingSegment;

        /// <summary>
        /// 用户选择的片段开始时间（秒）。
        /// </summary>
        public double SelectedStartSeconds { get; private set; }

        /// <summary>
        /// 用户选择的片段结束时间（秒）。
        /// </summary>
        public double SelectedEndSeconds { get; private set; }

        public SegmentSelectionWindow(string videoPath)
        {
            InitializeComponent();

            _videoPath = videoPath;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _timer.Tick += OnTimerTick;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PART_Media.Source = new Uri(_videoPath);
            PART_Media.MediaOpened += OnMediaOpened;
            PART_Media.MediaEnded += OnMediaEnded;

            _timer.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            PART_Media.MediaOpened -= OnMediaOpened;
            PART_Media.MediaEnded -= OnMediaEnded;
            PART_Media.Close();
        }

        private void OnMediaOpened(object? sender, RoutedEventArgs e)
        {
            if (PART_Media.NaturalDuration.HasTimeSpan)
            {
                var duration = PART_Media.NaturalDuration.TimeSpan;
                PART_Timeline.Minimum = 0;
                PART_Timeline.Maximum = duration.TotalSeconds;
                PART_DurationText.Text = $"总长: {FormatTime(duration)}";

                SelectedStartSeconds = 0;
                SelectedEndSeconds = duration.TotalSeconds;

                PART_StartText.Text = FormatTime(TimeSpan.Zero);
                PART_EndText.Text = FormatTime(duration);
            }
        }

        private void OnMediaEnded(object? sender, RoutedEventArgs e)
        {
            _isPlaying = false;
            _isPreviewingSegment = false;
            PART_PlayPauseButton.Content = "播放";
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if ((!_isPlaying && !_isPreviewingSegment) ||
                !PART_Media.NaturalDuration.HasTimeSpan)
            {
                return;
            }

            var position = PART_Media.Position;
            PART_Timeline.Value = position.TotalSeconds;
            PART_CurrentTimeText.Text = $"当前: {FormatTime(position)}";

            if (_isPreviewingSegment &&
                position.TotalSeconds >= SelectedEndSeconds)
            {
                PART_Media.Pause();
                _isPreviewingSegment = false;
                _isPlaying = false;
                PART_PlayPauseButton.Content = "播放";
            }
        }

        private static string FormatTime(TimeSpan time)
        {
            // mm:ss.0
            return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds / 100}";
        }

        private void OnPlayPauseClick(object sender, RoutedEventArgs e)
        {
            if (!_isPlaying)
            {
                PART_Media.Play();
                _isPlaying = true;
                _isPreviewingSegment = false;
                PART_PlayPauseButton.Content = "暂停";
            }
            else
            {
                PART_Media.Pause();
                _isPlaying = false;
                _isPreviewingSegment = false;
                PART_PlayPauseButton.Content = "播放";
            }
        }

        private void OnPreviewSegmentClick(object sender, RoutedEventArgs e)
        {
            if (!PART_Media.NaturalDuration.HasTimeSpan)
            {
                return;
            }

            var duration = PART_Media.NaturalDuration.TimeSpan.TotalSeconds;
            var start = Math.Max(0, Math.Min(SelectedStartSeconds, duration));
            var end = Math.Max(0, Math.Min(SelectedEndSeconds, duration));

            if (end <= start + 0.1)
            {
                // 片段太短，忽略
                return;
            }

            PART_Media.Position = TimeSpan.FromSeconds(start);
            PART_Media.Play();
            _isPlaying = true;
            _isPreviewingSegment = true;
            PART_PlayPauseButton.Content = "暂停";
        }

        private void OnSetStartFromCurrentClick(object sender, RoutedEventArgs e)
        {
            if (!PART_Media.NaturalDuration.HasTimeSpan)
            {
                return;
            }

            var pos = PART_Media.Position.TotalSeconds;
            var duration = PART_Media.NaturalDuration.TimeSpan.TotalSeconds;

            SelectedStartSeconds = Math.Clamp(pos, 0, duration);
            if (SelectedEndSeconds <= SelectedStartSeconds)
            {
                SelectedEndSeconds = Math.Min(duration, SelectedStartSeconds + 5.0);
            }

            PART_StartText.Text = FormatTime(TimeSpan.FromSeconds(SelectedStartSeconds));
            PART_EndText.Text = FormatTime(TimeSpan.FromSeconds(SelectedEndSeconds));
        }

        private void OnSetEndFromCurrentClick(object sender, RoutedEventArgs e)
        {
            if (!PART_Media.NaturalDuration.HasTimeSpan)
            {
                return;
            }

            var pos = PART_Media.Position.TotalSeconds;
            var duration = PART_Media.NaturalDuration.TimeSpan.TotalSeconds;

            SelectedEndSeconds = Math.Clamp(pos, 0, duration);
            if (SelectedEndSeconds <= SelectedStartSeconds)
            {
                SelectedStartSeconds = Math.Max(0, SelectedEndSeconds - 5.0);
            }

            PART_StartText.Text = FormatTime(TimeSpan.FromSeconds(SelectedStartSeconds));
            PART_EndText.Text = FormatTime(TimeSpan.FromSeconds(SelectedEndSeconds));
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            if (!PART_Media.NaturalDuration.HasTimeSpan)
            {
                DialogResult = false;
                return;
            }

            var duration = PART_Media.NaturalDuration.TimeSpan.TotalSeconds;
            var start = Math.Max(0, Math.Min(SelectedStartSeconds, duration));
            var end = Math.Max(0, Math.Min(SelectedEndSeconds, duration));

            if (end <= start + 0.1)
            {
                MessageBox.Show(this, "选择的片段太短，请重新选择。", "提示",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedStartSeconds = start;
            SelectedEndSeconds = end;

            DialogResult = true;
        }

        private void PART_Timeline_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!PART_Media.NaturalDuration.HasTimeSpan)
            {
                return;
            }

            var sec = e.NewValue;
            PART_Media.Position = TimeSpan.FromSeconds(sec);
            PART_CurrentTimeText.Text = $"当前: {FormatTime(PART_Media.Position)}";
        }
    }
}