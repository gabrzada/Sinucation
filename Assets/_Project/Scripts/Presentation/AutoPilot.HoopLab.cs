using System.Collections.Generic;
using System.Globalization;
using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// -fsHoopLab: laboratório da cesta de arcade em rede (host + clientes no mesmo PC, mesmo relógio de parede).
    /// Todos os processos registam a posição do aro com a hora de parede ([HoopTrack]) para comparar host e cliente,
    /// cada cesta confirmada pelo anfitrião ([HoopBasket]) e o que a física local de cada um viu ([HoopLocal]).
    /// Com -fsHoopThrows N este processo faz N arremessos pelo caminho real (NetGame.SendThrow, a mesma fórmula do [G]),
    /// a mirar com a câmara: certeiros, falhados de propósito e de baixo para cima através do aro (não pode contar).
    /// -fsHoopLobby S: o anfitrião espera S s na sala antes de começar a partida (cestas sem dinheiro na espera).
    /// </summary>
    public sealed partial class AutoPilot
    {
        static readonly string[] HoopThrowKinds = { "aim", "aim", "miss", "aim", "below" };
        static readonly Vector3[] HoopSpots = { new Vector3(4.2f, 0.05f, -0.9f), new Vector3(3.0f, 0.05f, -1.6f), new Vector3(5.2f, 0.05f, -1.6f) };

        bool _hoopHooked;
        float _hoopTrackAt, _hoopNextThrow = -1f;
        int _hoopThrows;
        readonly Dictionary<int, RimTrack> _hoopLocal = new Dictionary<int, RimTrack>();

        static string F(double v) => v.ToString("0.0000", CultureInfo.InvariantCulture);

        void UpdateHoopLab(NetGame net)
        {
            if (!Has("-fsHoopLab")) return;
            var hoop = _game.Hoop;
            if (hoop == null) return;
            string role = net.IsLocalHost ? "host" : "client" + net.LocalClientId;
            if (!_hoopHooked)
            {
                _hoopHooked = true;
                net.Basket += (thrower, id, credited) =>
                {
                    var me = net.LocalPlayer;
                    Log("[HoopBasket] role=" + role + " id=" + id + " thrower=" + thrower + " credited=" + credited +
                        " inMatch=" + net.View.InMatch + " myCoins=" + (me != null ? me.Coins : -1) + " wall=" + WallMs());
                };
                net.Thrown += t => Log("[HoopSpawn] role=" + role + " id=" + t.Id + " sender=" + t.Sender + " age=" + F(t.Age));
                net.CoinsChanged += (id, delta, reason) =>
                {
                    if (reason != null && reason.StartsWith("Cesta")) Log("[HoopCoins] role=" + role + " player=" + id + " delta=" + delta + " reason=" + reason);
                };
            }

            // Posição do aro vista por este peer, 10× por segundo, com a hora de parede (comum aos processos do mesmo PC).
            if (_time >= _hoopTrackAt)
            {
                _hoopTrackAt = _time + 0.1f;
                var rim = hoop.RimCenter;
                Log("[HoopTrack] role=" + role + " wall=" + WallMs() + " net=" + F(net.NetworkTime) + " x=" + F(rim.x) +
                    " expect=" + F(RoomBuilder.HoopAnchor.position.x + HoopRules.Offset(net.NetworkTime)));
            }

            // Física local de cada peer (só registo): o objecto entrou no aro aqui também?
            foreach (var prop in ThrownProp.Live)
            {
                if (prop == null || prop.Body == null) continue;
                _hoopLocal.TryGetValue(prop.ThrowId, out var track);
                if (track.Step(prop.Body.position - hoop.RimCenter, prop.Body.linearVelocity.y, prop.Radius, true))
                    Log("[HoopLocal] role=" + role + " id=" + prop.ThrowId + " in=1");
                _hoopLocal[prop.ThrowId] = track;
            }

            UpdateHoopThrows(net, hoop);
        }

        static long WallMs() => System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        void UpdateHoopThrows(NetGame net, ArcadeHoop hoop)
        {
            int total = IntArg("-fsHoopThrows", 0);
            var local = NetPlayer.Local;
            if (_hoopThrows >= total || local == null || _game.Shot.UsesShotCamera) return;
            var view = net.View;
            var me = net.LocalPlayer;
            // Na partida só atira enquanto espera pela vez (como um jogador a sério).
            if (view.InMatch && (me == null || view.CurrentPlayer == me.Id || view.Phase == MatchPhase.MatchOver || view.MatchPaused)) return;
            if (_hoopNextThrow < 0f) _hoopNextThrow = _time + 4f;
            if (_time < _hoopNextThrow) return;
            _hoopNextThrow = _time + 3.2f;

            string kind = HoopThrowKinds[_hoopThrows % HoopThrowKinds.Length];
            Vector3 feet = kind == "below" ? new Vector3(3.55f, 0.05f, -5.35f) : HoopSpots[_hoopThrows % HoopSpots.Length];
            local.TeleportTo(feet, 180f);
            Vector3 eye = feet + Vector3.up * AvatarVisual.EyeHeight;
            // Onde o aro vai estar: relógio de rede + voo (+ um pouco do caminho até ao anfitrião).
            double now = net.NetworkTime + (net.IsLocalHost ? 0.0 : 0.03);
            float side = kind == "miss" ? 0.75f : 0f;
            if (!SolveHoopThrow(eye, now, side, kind == "below", out float yaw, out float pitch, out float flight))
            {
                Log("[HoopThrow] role=" + (net.IsLocalHost ? "host" : "client") + " n=" + _hoopThrows + " kind=" + kind + " sem solução");
                _hoopThrows++;
                return;
            }
            local.SetLook(yaw, -pitch);
            var rot = Quaternion.Euler(-pitch, yaw, 0f);
            Vector3 forward = rot * Vector3.forward, up = rot * Vector3.up;
            // A mesma fórmula do ThrowablesView ([G]).
            net.SendThrow(eye + forward * 0.45f - up * 0.1f, forward * 7.5f + Vector3.up * 1.2f);
            Log("[HoopThrow] role=" + (net.IsLocalHost ? "host" : "client") + " n=" + _hoopThrows + " kind=" + kind + " inMatch=" + view.InMatch +
                " yaw=" + F(yaw) + " pitch=" + F(pitch) + " flight=" + F(flight) + " wall=" + WallMs());
            _hoopThrows++;
        }

        /// <summary>
        /// Procura a direcção da câmara (velocidade do [G]) cujo voo, com o amortecimento do Rigidbody, atravessa o plano do
        /// aro no centro do aro previsto para esse instante: a descer (cesta) ou, com <paramref name="upward"/>, a subir.
        /// </summary>
        static bool SolveHoopThrow(Vector3 eye, double now, float side, bool upward, out float bestYaw, out float bestPitch, out float bestFlight)
        {
            float foundYaw = 180f, foundPitch = 45f, foundFlight = 0f;
            var anchor = RoomBuilder.HoopAnchor;
            Vector3 RimAt(double t) => anchor.position + anchor.rotation * new Vector3(HoopRules.Offset(t) + side, ArcadeHoop.RimY, ArcadeHoop.RimOut);
            float best = float.MaxValue;
            var guess = RimAt(now + 1.0) - eye;
            float baseYaw = Mathf.Atan2(guess.x, guess.z) * Mathf.Rad2Deg;

            void Search(float yaw0, float yawRange, float yawStep, float pitch0, float pitch1, float pitchStep)
            {
                for (float yaw = yaw0 - yawRange; yaw <= yaw0 + yawRange; yaw += yawStep)
                    for (float pitch = pitch0; pitch <= pitch1; pitch += pitchStep)
                    {
                        var rot = Quaternion.Euler(-pitch, yaw, 0f);
                        Vector3 f = rot * Vector3.forward, u = rot * Vector3.up;
                        Vector3 p = eye + f * 0.45f - u * 0.1f, v = f * 7.5f + Vector3.up * 1.2f;
                        const float dt = 0.02f;
                        for (float t = 0f; t < 2.5f; t += dt)
                        {
                            var rim = RimAt(now + t);
                            float before = p.y - rim.y;
                            v += Physics.gravity * dt;
                            v *= 1f / (1f + dt * 0.2f);
                            p += v * dt;
                            float after = p.y - RimAt(now + t + dt).y;
                            bool crossed = upward ? before <= 0f && after > 0f : before > 0f && after <= 0f && v.y < 0f;
                            if (!crossed) continue;
                            var at = RimAt(now + t + dt);
                            float miss = new Vector2(p.x - at.x, p.z - at.z).magnitude;
                            if (miss < best)
                            {
                                best = miss;
                                foundYaw = yaw; foundPitch = pitch; foundFlight = t + dt;
                            }
                            break;
                        }
                    }
            }

            float lo = upward ? 55f : 20f, hi = upward ? 88f : 75f;
            Search(baseYaw, 35f, 1f, lo, hi, 1f);
            float y0 = foundYaw, p0 = foundPitch;
            Search(y0, 1f, 0.1f, Mathf.Max(lo, p0 - 1f), Mathf.Min(hi, p0 + 1f), 0.1f);
            bestYaw = foundYaw; bestPitch = foundPitch; bestFlight = foundFlight;
            return best < 0.06f;
        }
    }
}
