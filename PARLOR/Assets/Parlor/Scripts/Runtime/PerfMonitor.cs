using UnityEngine;
using Unity.Profiling;

namespace Parlor
{
    /// <summary>
    /// Frame-time evidence per beat: when a beat ends it logs the average frame rate, the worst frame, and how many
    /// frames missed the display's budget, plus main-thread CPU time and per-frame GC allocation where the platform
    /// exposes those counters. Cheap (a few adds a frame, one log line a beat); runs in release builds so a device run
    /// records the same numbers in logcat.
    /// </summary>
    public sealed class PerfMonitor : MonoBehaviour
    {
        ShowDirector _director;
        string _beat = "Startup";
        int _frames, _over;
        float _sum, _worst, _cpuSum, _cpuWorst;
        long _gcSum;
        ProfilerRecorder _cpu, _gc;

        public static PerfMonitor Attach(GameObject host, ShowDirector director)
        {
            var m = host.AddComponent<PerfMonitor>();
            m._director = director;
            director.BeatStarted += m.OnBeat;
            return m;
        }

        void OnEnable()
        {
            try { _cpu = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1); } catch { }
            try { _gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1); } catch { }
        }

        void OnDisable()
        {
            if (_cpu.Valid) _cpu.Dispose();
            if (_gc.Valid) _gc.Dispose();
        }

        void OnDestroy() { if (_director) _director.BeatStarted -= OnBeat; }

        /// <summary>The display refresh rate, set by the platform layer (QuestBoot); 72 Hz until told otherwise.</summary>
        public static float DisplayHz = 72f;
        static float Budget() => 1000f / Mathf.Max(30f, DisplayHz);

        void Update()
        {
            if (_director && _director.Paused) return;
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms <= 0f || ms > 1000f) return;          // a stall from pausing or the Editor, not a frame
            _frames++; _sum += ms; _worst = Mathf.Max(_worst, ms);
            if (ms > Budget() * 1.5f) _over++;
            if (_cpu.Valid && _cpu.LastValue > 0) { float c = _cpu.LastValue / 1e6f; _cpuSum += c; _cpuWorst = Mathf.Max(_cpuWorst, c); }
            if (_gc.Valid) _gcSum += _gc.LastValue;
        }

        void OnBeat(ShowState next)
        {
            if (_frames > 10)
            {
                string cpu = _cpu.Valid && _cpuSum > 0 ? $", cpu {_cpuSum / _frames:F1} ms avg / {_cpuWorst:F1} worst" : "";
                string gc = _gc.Valid ? $", gc {_gcSum / (float)_frames / 1024f:F2} KB/frame" : "";
                Debug.Log($"[PARLOR] perf {_beat}: {1000f * _frames / _sum:F0} fps avg, worst {_worst:F1} ms, {_over}/{_frames} frames over budget ({Budget():F1} ms){cpu}{gc}");
            }
            _beat = next.ToString();
            _frames = _over = 0; _sum = _worst = _cpuSum = _cpuWorst = 0f; _gcSum = 0;
        }
    }
}
