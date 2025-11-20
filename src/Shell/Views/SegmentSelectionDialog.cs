#nullable enable

using System.Windows;

namespace EasyCut.Views
{
    /// <summary>
    /// 片段选择对话框帮助类。
    /// </summary>
    public static class SegmentSelectionDialog
    {
        /// <summary>
        /// 打开片段选择对话框，让用户选择一个视频片段。
        /// </summary>
        /// <param name="videoPath">原始视频路径。</param>
        /// <param name="startSeconds">返回的起始时间（秒）。</param>
        /// <param name="endSeconds">返回的结束时间（秒）。</param>
        /// <returns>用户确认返回 true，取消返回 false。</returns>
        public static bool TrySelectSegment(
            string videoPath,
            out double startSeconds,
            out double endSeconds)
        {
            var window = new SegmentSelectionWindow(videoPath)
            {
                Owner = Application.Current?.MainWindow
            };

            var result = window.ShowDialog();
            if (result == true)
            {
                startSeconds = window.SelectedStartSeconds;
                endSeconds = window.SelectedEndSeconds;
                return true;
            }

            startSeconds = 0;
            endSeconds = 0;
            return false;
        }
    }
}