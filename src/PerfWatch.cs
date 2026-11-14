using System;
using System.Text;

namespace MaydayCareer
{
    /// <summary>
    /// 帧时间监测 —— 用来回答"地面粘滞感到底是谁的锅"。
    ///
    /// 设计原则：监测本身不能成为负担。
    ///   每帧只做一次 unscaledDeltaTime 读取 + 比较（约几十纳秒）；
    ///   每 60 帧才开一次 Stopwatch 量插件整段 Tick 的耗时（约每秒一次）；
    ///   每 10 秒汇总一次，而且只有真的出现卡顿帧才写日志。
    ///
    /// 用法（见 TickHook）：
    ///   Postfix 开头 PerfWatch.OnFrameBegin()，结尾 OnFrameEnd()。
    ///   OnFrameBegin 记录帧时长与卡顿帧；采样帧顺带量出"插件每帧 ≤X ms"，
    ///   与游戏的卡顿帧放同一条日志里，直接对比归因。
    /// </summary>
    internal static class PerfWatch
    {
        /// <summary>超过这个帧时长算"卡顿帧"（秒）。100ms ≈ 明显掉帧的感觉。</summary>
        const float HitchThreshold = 0.10f;

        /// <summary>汇总窗口（秒）。</summary>
        const float WindowSeconds = 10f;

        /// <summary>每 N 帧采样一次插件自身耗时（避免每帧 Stopwatch 的固定开销）。</summary>
        const int SampleEveryFrames = 60;

        static bool _enabled = true;
        static int _sampleCountdown = SampleEveryFrames;
        static readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        static double _selfMsMax;          // 窗口内插件 Tick 的最长采样（毫秒）
        static int _hitches;               // 窗口内卡顿帧数
        static float _hitchWorst;          // 最长卡顿帧（秒）
        static float _windowElapsed;       // 窗口已累计时间（秒）

        public static void Configure(bool enabled)
        {
            _enabled = enabled;
            if (!enabled) Plugin.LogSource.LogInfo("[MaydayCareer] 帧监测已关闭（PerfWatch=false）");
        }

        /// <summary>每帧 Postfix 开头调用。开销：一次属性读取 + 比较。</summary>
        public static void OnFrameBegin()
        {
            if (!_enabled) return;
            try
            {
                float dt = UnityEngine.Time.unscaledDeltaTime;
                _windowElapsed += dt;
                if (dt > HitchThreshold)
                {
                    _hitches++;
                    if (dt > _hitchWorst) _hitchWorst = dt;
                }

                if (--_sampleCountdown <= 0)
                {
                    _sampleCountdown = SampleEveryFrames;
                    _sw.Restart();
                }
                else
                {
                    _sw.Reset(); // 非采样帧保持"未计时"状态
                }
            }
            catch { /* 监测绝不能把游戏搞挂 */ }
        }

        /// <summary>每帧 Postfix 结尾调用。只有采样帧会真正停表记录。</summary>
        public static void OnFrameEnd()
        {
            if (!_enabled) return;
            try
            {
                if (_sw.IsRunning)
                {
                    _sw.Stop();
                    double ms = _sw.Elapsed.TotalMilliseconds;
                    if (ms > _selfMsMax) _selfMsMax = ms;
                }

                if (_windowElapsed >= WindowSeconds) Report();
            }
            catch { }
        }

        static void Report()
        {
            try
            {
                float window = _windowElapsed;
                _windowElapsed = 0f;
                int hitches = _hitches; _hitches = 0;
                float worst = _hitchWorst; _hitchWorst = 0f;
                double self = _selfMsMax; _selfMsMax = 0;

                if (hitches == 0) return; // 窗口很干净，保持沉默

                var sb = new StringBuilder();
                sb.Append(string.Format(
                    "[MaydayCareer] 帧监测（近 {0:F0}s）：{1} 个 >{2:F0}ms 卡顿帧，最长 {3:F0}ms",
                    window, hitches, HitchThreshold * 1000f, worst * 1000f));

                try
                {
                    var gd = UnityEngine.Object.FindObjectOfType<global::Mayday.Airport.GameDirector>();
                    if (gd != null) sb.Append("（阶段=" + gd.Stage + "）");
                }
                catch { }

                sb.Append(string.Format("，插件自身每帧 ≤{0:F2}ms", self));
                Plugin.LogSource.LogWarning(sb.ToString());
            }
            catch { }
        }
    }
}
