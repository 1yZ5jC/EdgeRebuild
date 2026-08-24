using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.SpeechSynthesis;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace EdgeRebuild.Services
{
    /// <summary>
    /// 大声朗读：Windows.Media.SpeechSynthesis（系统 TTS 语音）+ MediaElement 播放。
    /// 文本按句子切分后逐段合成播放，支持暂停/恢复/停止。
    /// </summary>
    public static class ReadAloudService
    {
        private static SpeechSynthesizer _synth;
        private static MediaElement _player;
        private static Windows.UI.Core.CoreDispatcher _playerDispatcher;   // 播放器所属视图的调度器
        private static readonly List<string> _paragraphs = new List<string>();
        private static int _index = 0;
        private static bool _active = false;
        private static bool _paused = false;
        private static string _currentText = "";

        /// <summary>朗读状态变化（供 UI 刷新按钮文字）。</summary>
        public static event Action StateChanged;

        public static bool IsActive => _active;
        public static bool IsPaused => _paused;
        public static string CurrentLabel => _currentText;

        /// <summary>
        /// 真多视图：播放器固定在首个初始化它的视图（通常主视图）线程上；
        /// 任何窗口调用 Start/Pause/Stop 都经此调度器封送回该线程执行。
        /// </summary>
        public static bool Initialize(MediaElement player)
        {
            try
            {
                if (_player != null && !ReferenceEquals(_player, player)) return true;   // 已由其他视图初始化
                _player = player;
                _playerDispatcher = Window.Current.CoreWindow.Dispatcher;
                _player.MediaEnded += OnMediaEnded;
                _player.MediaFailed += (s, e) => Stop();
                _synth = new SpeechSynthesizer();
                return true;
            }
            catch (Exception ex)
            {
                _player = null;
                _synth = null;
                Services.Diagnostics.Log($"[朗读] 初始化失败（可能缺少系统 TTS 语音）: {ex.Message}");
                return false;
            }
        }

        private static void RunOnPlayerThread(Action action)
        {
            var d = _playerDispatcher ?? Window.Current.CoreWindow.Dispatcher;
            var ignored = d.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            {
                try { action(); } catch (Exception ex) { Services.Diagnostics.Log($"[朗读] 播放操作异常: {ex.Message}"); }
            });
        }

        /// <summary>开始朗读（自动替换当前朗读）。任意视图线程可调，内部封送到播放器线程。</summary>
        public static void Start(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_synth == null || _player == null)
            {
                Services.Diagnostics.Log("[朗读] 组件未初始化，无法朗读");
                return;
            }
            RunOnPlayerThread(() =>
            {
                Stop();
                _currentText = label ?? "";
                _paragraphs.Clear();
                _paragraphs.AddRange(SplitParagraphs(text));
                _index = 0;
                _active = true;
                _paused = false;
                PlayNext();
                StateChanged?.Invoke();
            });
        }

        /// <summary>暂停/恢复切换。</summary>
        public static void TogglePause()
        {
            RunOnPlayerThread(() =>
            {
                if (!_active || _player == null) return;
                if (_paused)
                {
                    _player.Play();
                    _paused = false;
                }
                else
                {
                    _player.Pause();
                    _paused = true;
                }
                StateChanged?.Invoke();
            });
        }

        public static void Stop()
        {
            // Stop 也可能由播放器线程自身（MediaFailed/PlayNext）调用——同线程 RunAsync 仍按序执行
            RunOnPlayerThread(StopCore);
        }

        private static void StopCore()
        {
            _active = false;
            _paused = false;
            _index = 0;
            try { _player?.Stop(); if (_player != null) _player.Source = null; } catch { }
            _paragraphs.Clear();
            StateChanged?.Invoke();
        }

        private static async void PlayNext()
        {
            if (!_active) return;
            if (_index >= _paragraphs.Count)
            {
                StopCore();
                return;
            }
            try
            {
                var stream = await _synth.SynthesizeTextToStreamAsync(_paragraphs[_index]);
                if (!_active) return;
                _player.SetSource(stream, stream.ContentType);
                _player.Play();
            }
            catch
            {
                StopCore();
            }
        }

        private static void OnMediaEnded(object sender, RoutedEventArgs e)
        {
            if (!_active) return;
            _index++;
            PlayNext();
        }

        /// <summary>按句子切分，每段不超过约 380 字（避免单段过长合成卡顿）。</summary>
        private static IEnumerable<string> SplitParagraphs(string text)
        {
            var parts = text.Split(new[] { '。', '！', '？', '!', '?', '；', ';', '\n', '\r', '\t', '\u3000' },
                StringSplitOptions.RemoveEmptyEntries);
            var buf = new StringBuilder();
            foreach (var raw in parts)
            {
                string s = raw.Trim();
                if (s.Length == 0) continue;
                if (buf.Length + s.Length > 380)
                {
                    yield return buf.ToString();
                    buf.Clear();
                }
                buf.Append(s);
                if (!s.EndsWith("。", StringComparison.Ordinal)
                    && !s.EndsWith("！", StringComparison.Ordinal)
                    && !s.EndsWith("？", StringComparison.Ordinal))
                    buf.Append("。");
            }
            if (buf.Length > 0) yield return buf.ToString();
        }
    }
}
