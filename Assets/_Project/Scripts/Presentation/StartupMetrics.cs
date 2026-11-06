using System.Diagnostics;
using UnityEngine;

namespace FriendSlop.Presentation
{
    public static class StartupMetrics
    {
        static Stopwatch s_watch;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Start() { s_watch = Stopwatch.StartNew(); }
        public static void Mark(string stage)
        {
            if (s_watch != null) UnityEngine.Debug.Log($"[Startup] {stage}: {s_watch.Elapsed.TotalMilliseconds:F1} ms");
        }
    }
}
