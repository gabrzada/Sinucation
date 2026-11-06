using System;
using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Core
{
    public enum RimCrossing
    {
        None,
        /// <summary>Atravessou o plano do aro a descer, por dentro: cesta.</summary>
        Down,
        /// <summary>Atravessou o plano do aro a subir, por dentro (vindo de baixo da rede): esse lançamento já não conta.</summary>
        Up
    }

    /// <summary>
    /// Cesta de arcade móvel (parede sul): movimento determinístico a partir do tempo de rede, para todos os peers verem o
    /// aro no mesmo sítio sem replicar posições, e geometria da cesta — só conta o objecto que entra por cima, a descer.
    /// </summary>
    public static class HoopRules
    {
        /// <summary>Raio interior do aro (m).</summary>
        public const float RimRadius = 0.24f;
        /// <summary>Deslocamento máximo para cada lado do centro do curso (m).</summary>
        public const float SwayAmplitude = 1.0f;
        /// <summary>Período de uma ida e volta (s): pico de ~0,9 m/s, abranda nas pontas (seno, sem inversões bruscas).</summary>
        public const float SwayPeriod = 7f;
        /// <summary>Saltos maiores do que isto entre dois passos de física são teleportes, não trajectórias.</summary>
        const float MaxStep = 1f;

        /// <summary>Deslocamento lateral do aro no instante <paramref name="serverTime"/> (relógio de rede do NGO).</summary>
        public static float Offset(double serverTime)
        {
            double phase = serverTime % SwayPeriod;
            if (phase < 0) phase += SwayPeriod;
            return SwayAmplitude * (float)Math.Sin(phase / SwayPeriod * Math.PI * 2.0);
        }

        /// <summary>
        /// Classifica o passo de física de um objecto em relação ao aro. Posições relativas ao centro do aro (Y para cima,
        /// plano do aro em Y = 0). Conta se o ponto onde cruza o plano fica dentro do aro, descontando meio raio do objecto.
        /// </summary>
        public static RimCrossing Classify(Vector3 previousRel, Vector3 currentRel, float velocityY, float propRadius, float rimRadius = RimRadius)
        {
            if ((currentRel - previousRel).sqrMagnitude > MaxStep * MaxStep) return RimCrossing.None;
            bool down = previousRel.y > 0f && currentRel.y <= 0f;
            bool up = previousRel.y <= 0f && currentRel.y > 0f;
            if (!down && !up) return RimCrossing.None;
            float t = previousRel.y / (previousRel.y - currentRel.y);
            var cross = Vector3.Lerp(previousRel, currentRel, t);
            float inside = rimRadius - propRadius * 0.5f;
            if (cross.x * cross.x + cross.z * cross.z > inside * inside) return RimCrossing.None;
            if (down && velocityY < 0f) return RimCrossing.Down;
            if (up && velocityY > 0f) return RimCrossing.Up;
            return RimCrossing.None;
        }
    }

    /// <summary>
    /// Estado de um objecto atirado em relação ao aro, passo a passo. Só a autoridade (anfitrião) o avança: devolve
    /// verdadeiro uma única vez, no passo em que o objecto entra por cima; quem passou primeiro por baixo já não conta.
    /// </summary>
    public struct RimTrack
    {
        public Vector3 LastRelative;
        public bool HasLast;
        public bool CameFromBelow;
        public bool Scored;

        public bool Step(Vector3 rimRelative, float velocityY, float propRadius, bool authority)
        {
            if (!authority) return false;
            bool scored = false;
            if (HasLast && !Scored)
            {
                var crossing = HoopRules.Classify(LastRelative, rimRelative, velocityY, propRadius);
                if (crossing == RimCrossing.Up) CameFromBelow = true;
                else if (crossing == RimCrossing.Down && !CameFromBelow) scored = Scored = true;
            }
            LastRelative = rimRelative;
            HasLast = true;
            return scored;
        }
    }

    /// <summary>
    /// Registo autoritativo dos lançamentos (só existe no anfitrião): cada arremesso aceite recebe um id, e cada id pode
    /// pontuar uma única vez. Um objecto atirado não se apanha; para voltar a pontuar é preciso um lançamento novo.
    /// </summary>
    public sealed class HoopLedger
    {
        /// <summary>Lançamentos mais velhos do que isto já desapareceram (ThrowablesView.Lifetime = 12 s).</summary>
        public const double ThrowLifetime = 15.0;

        readonly Dictionary<int, (ulong thrower, double at)> _open = new Dictionary<int, (ulong, double)>();
        readonly List<int> _expired = new List<int>();
        int _next;

        public int OpenThrows => _open.Count;

        public int Register(ulong thrower, double now)
        {
            Prune(now);
            int id = ++_next;
            _open[id] = (thrower, now);
            return id;
        }

        /// <summary>Verdadeiro uma única vez por lançamento conhecido e ainda vivo; devolve quem o atirou.</summary>
        public bool TryClaim(int throwId, double now, out ulong thrower)
        {
            thrower = 0;
            if (!_open.TryGetValue(throwId, out var entry)) return false;
            _open.Remove(throwId);
            if (now - entry.at > ThrowLifetime) return false;
            thrower = entry.thrower;
            return true;
        }

        public void Clear()
        {
            _open.Clear();
            _next = 0;
        }

        void Prune(double now)
        {
            _expired.Clear();
            foreach (var kv in _open)
                if (now - kv.Value.at > ThrowLifetime) _expired.Add(kv.Key);
            foreach (int id in _expired) _open.Remove(id);
        }
    }
}
