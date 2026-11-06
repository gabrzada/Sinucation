using System.Collections.Generic;
using FriendSlop.Billiards;
using UnityEngine;

namespace FriendSlop.Net
{
    /// <summary>
    /// Interpolação de snapshots de bolas no cliente, com ~100 ms de atraso. Só apresentação:
    /// a verdade é sempre o snapshot final do servidor (NFR-001).
    /// </summary>
    public sealed class BallInterpolator
    {
        const float Delay = 0.1f;
        const int MaxFrames = 48;

        struct Frame
        {
            public float Time;
            public List<BallState> Balls;
        }

        readonly List<Frame> _frames = new List<Frame>();
        float _renderTime;

        public bool HasFrames => _frames.Count > 0;

        public void Push(float serverTime, List<BallState> balls)
        {
            if (_frames.Count == 0) _renderTime = serverTime - Delay;
            _frames.Add(new Frame { Time = serverTime, Balls = new List<BallState>(balls) });
            if (_frames.Count > MaxFrames) _frames.RemoveAt(0);
        }

        public void Reset() => _frames.Clear();

        public void Sample(float deltaTime, List<BallState> output)
        {
            output.Clear();
            if (_frames.Count == 0) return;

            float newest = _frames[_frames.Count - 1].Time;
            _renderTime += deltaTime;
            if (newest - _renderTime > 0.3f) _renderTime = newest - Delay;
            _renderTime = Mathf.Clamp(_renderTime, _frames[0].Time, newest);

            int i = 0;
            while (i < _frames.Count - 2 && _frames[i + 1].Time <= _renderTime) i++;
            var a = _frames[i];
            var b = _frames[Mathf.Min(i + 1, _frames.Count - 1)];
            float span = b.Time - a.Time;
            float t = span > 1e-4f ? Mathf.Clamp01((_renderTime - a.Time) / span) : 1f;

            foreach (var ballA in a.Balls)
            {
                var result = ballA;
                foreach (var ballB in b.Balls)
                {
                    if (ballB.Number != ballA.Number) continue;
                    if (!ballA.Pocketed && !ballB.Pocketed) result.Position = Vector2.Lerp(ballA.Position, ballB.Position, t);
                    else if (t > 0.5f) result = ballB;
                    break;
                }
                output.Add(result);
            }

            // Descarta frames que já ficaram para trás.
            while (_frames.Count > 2 && _frames[1].Time < _renderTime - 0.5f) _frames.RemoveAt(0);
        }
    }
}
