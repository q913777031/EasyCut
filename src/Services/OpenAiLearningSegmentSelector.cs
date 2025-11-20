#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAI.Chat;

namespace EasyCut.Services
{
    /// <summary>
    /// OpenAI 模型驱动的学习片段选择器：
    /// - 先根据字幕构造若干候选片段；
    /// - 调用 gpt-4o-mini 让模型根据学习价值选择一个 index；
    /// - 解析模型返回的 JSON，得到最终片段；
    /// - 出错时回退到规则版选择器。
    /// </summary>
    public sealed class OpenAiLearningSegmentSelector : ILearningSegmentSelector
    {
        /// <summary>
        /// OpenAI 聊天客户端。
        /// </summary>
        private readonly ChatClient _chatClient;

        /// <summary>
        /// 回退用的规则选择器（仍然实现了 ILearningSegmentSelector）。
        /// </summary>
        private readonly ILearningSegmentSelector _fallbackSelector;

        /// <summary>
        /// 最多生成多少个候选片段，避免 prompt 过长。
        /// </summary>
        private readonly int _maxCandidates;

        /// <summary>
        /// 构造函数。
        /// 注意：这里显式依赖 <see cref="RuleBasedLearningSegmentSelector"/>，
        /// 避免容器解析时出现 ILearningSegmentSelector 的循环依赖。
        /// </summary>
        /// <param name="chatClient">OpenAI 聊天客户端。</param>
        /// <param name="fallbackSelector">规则版回退选择器。</param>
        /// <param name="maxCandidates">候选片段最大数量。</param>
        public OpenAiLearningSegmentSelector(
            ChatClient chatClient,
            RuleBasedLearningSegmentSelector fallbackSelector,
            int maxCandidates = 30)
        {
            _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
            _fallbackSelector = fallbackSelector ?? throw new ArgumentNullException(nameof(fallbackSelector));
            _maxCandidates = maxCandidates > 0 ? maxCandidates : 30;
        }

        /// <summary>
        /// 选择学习片段时间范围。
        /// </summary>
        public async Task<(double StartSeconds, double EndSeconds)> SelectAsync(
            IReadOnlyList<SrtEntry> subtitles,
            double totalDurationSeconds,
            CancellationToken cancellationToken = default)
        {
            // 没有字幕或总时长异常时直接回退。
            if (subtitles.Count == 0 || totalDurationSeconds <= 0)
            {
                return await _fallbackSelector.SelectAsync(subtitles, totalDurationSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 先本地构造候选片段列表。
            var candidates = BuildCandidates(subtitles, totalDurationSeconds);
            if (candidates.Count == 0)
            {
                return await _fallbackSelector.SelectAsync(subtitles, totalDurationSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }

            try
            {
                // 构造提示词。
                string prompt = BuildPrompt(candidates, totalDurationSeconds);

                // 调用 OpenAI Chat 接口。
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(
                        "You are a JSON-only engine that chooses the best learning segment from candidates. " +
                        "You MUST respond with a single JSON object and nothing else."),
                    new UserChatMessage(prompt)
                };

                ChatCompletion completion = await _chatClient
                    .CompleteChatAsync(messages, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                string content = string.Join(
                    Environment.NewLine,
                    completion.Content
                        .Select(c => c.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t)));

                int? index = TryParseIndex(content);
                if (index.HasValue)
                {
                    var candidate = candidates.FirstOrDefault(x => x.Index == index.Value);
                    if (candidate is not null)
                    {
                        return (candidate.StartSeconds, candidate.EndSeconds);
                    }
                }

                // 解析失败时回退。
                return await _fallbackSelector.SelectAsync(subtitles, totalDurationSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // 调用异常时回退。
                return await _fallbackSelector.SelectAsync(subtitles, totalDurationSeconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 构造候选片段列表（从字幕窗口组合而成）。
        /// </summary>
        private List<LearningSegmentCandidate> BuildCandidates(
            IReadOnlyList<SrtEntry> subtitles,
            double totalDurationSeconds)
        {
            const double minDuration = 4.0;
            double maxDuration = Math.Min(totalDurationSeconds, 45.0);

            var result = new List<LearningSegmentCandidate>();

            for (int i = 0; i < subtitles.Count; i++)
            {
                var start = subtitles[i].Start;
                var end = subtitles[i].End;

                var lines = new List<string>(subtitles[i].Lines);

                for (int j = i; j < subtitles.Count; j++)
                {
                    if (j > i)
                    {
                        var s = subtitles[j];
                        end = s.End;
                        lines.AddRange(s.Lines);
                    }

                    double duration = (end - start).TotalSeconds;

                    if (duration < minDuration)
                    {
                        continue;
                    }

                    if (duration > maxDuration)
                    {
                        break;
                    }

                    string text = string.Join(" ", lines.Select(l => l.Trim()));
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    int wordCount = CountWords(text);
                    if (wordCount < 5)
                    {
                        continue;
                    }

                    var candidate = new LearningSegmentCandidate
                    {
                        Index = result.Count + 1,
                        StartSeconds = start.TotalSeconds,
                        EndSeconds = end.TotalSeconds,
                        Text = text,
                        WordCount = wordCount
                    };

                    result.Add(candidate);

                    if (result.Count >= _maxCandidates)
                    {
                        return result;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 构造发送给 OpenAI 的提示词文本。
        /// </summary>
        private static string BuildPrompt(
            IReadOnlyList<LearningSegmentCandidate> candidates,
            double totalDurationSeconds)
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are selecting the best single learning segment from an English video.");
            sb.AppendLine($"The total video duration is about {totalDurationSeconds:F1} seconds.");
            sb.AppendLine();
            sb.AppendLine("You are given a list of candidate segments. Each candidate has:");
            sb.AppendLine("- index");
            sb.AppendLine("- start time (seconds)");
            sb.AppendLine("- end time (seconds)");
            sb.AppendLine("- duration (seconds)");
            sb.AppendLine("- transcript text");
            sb.AppendLine();
            sb.AppendLine("Your goal: choose EXACTLY ONE candidate that is best for an intermediate English learner to study.");
            sb.AppendLine("Guidelines:");
            sb.AppendLine("- Prefer complete sentences with natural, everyday language.");
            sb.AppendLine("- Prefer segments with 10–40 words and 5–40 seconds of duration.");
            sb.AppendLine("- Avoid segments that are obviously cut off in the middle of a sentence.");
            sb.AppendLine("- Avoid segments that are mostly filler words or noises.");
            sb.AppendLine("- Do not create a new segment. You must pick one of the candidates by its index.");
            sb.AppendLine();
            sb.AppendLine("Return ONLY a single JSON object in this exact format (no markdown, no explanation):");
            sb.AppendLine("{\"index\": <the integer index of the best candidate>}");
            sb.AppendLine();
            sb.AppendLine("Candidates:");

            foreach (var c in candidates)
            {
                sb.AppendLine(
                    $"[{c.Index}] start={c.StartSeconds:F1}s, end={c.EndSeconds:F1}s, " +
                    $"duration={(c.EndSeconds - c.StartSeconds):F1}s, words={c.WordCount}");
                sb.AppendLine($"text: {c.Text}");
                sb.AppendLine("----");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 从模型返回内容中解析 index。
        /// </summary>
        private static int? TryParseIndex(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string trimmed = content.Trim();

            // 先尝试直接解析为纯数字。
            if (int.TryParse(trimmed, out int directIndex))
            {
                return directIndex;
            }

            // 再尝试按 JSON 解析。
            try
            {
                using JsonDocument doc = JsonDocument.Parse(trimmed);
                if (doc.RootElement.TryGetProperty("index", out JsonElement indexElement) &&
                    indexElement.TryGetInt32(out int value))
                {
                    return value;
                }
            }
            catch
            {
                // 忽略解析异常。
            }

            return null;
        }

        /// <summary>
        /// 简单统计英文单词数量。
        /// </summary>
        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            var parts = text
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length;
        }

        /// <summary>
        /// 学习片段候选数据模型。
        /// </summary>
        private sealed class LearningSegmentCandidate
        {
            /// <summary>
            /// 候选索引（从 1 开始）。
            /// </summary>
            public int Index { get; set; }

            /// <summary>
            /// 片段开始时间（秒）。
            /// </summary>
            public double StartSeconds { get; set; }

            /// <summary>
            /// 片段结束时间（秒）。
            /// </summary>
            public double EndSeconds { get; set; }

            /// <summary>
            /// 片段文本内容。
            /// </summary>
            public string Text { get; set; } = string.Empty;

            /// <summary>
            /// 文本单词数量。
            /// </summary>
            public int WordCount { get; set; }
        }
    }
}