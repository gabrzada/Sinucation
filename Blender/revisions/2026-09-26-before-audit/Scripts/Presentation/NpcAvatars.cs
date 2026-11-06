using System.Collections.Generic;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Avatares dos bots. Não são objectos de rede: cada cliente calcula a mesma posição a partir do
    /// MatchView replicado (lugar à volta da mesa, atrás da branca quando jogam, sofá quando eliminados).
    /// </summary>
    public sealed class NpcAvatars : MonoBehaviour
    {
        /// <summary>Com 'Enter Play Mode Options' (sem recarregar o domínio) os estáticos sobrevivem entre Plays: repõe-os.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            Instance = null;
        }

        const float WalkSpeed = 1.7f;

        public static NpcAvatars Instance { get; private set; }

        readonly Dictionary<int, AvatarVisual> _bySlot = new Dictionary<int, AvatarVisual>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly Dictionary<AvatarVisual, Vector3> _velocity = new Dictionary<AvatarVisual, Vector3>();

        void Awake() => Instance = this;

        NetGame _bound;

        /// <summary>Os bots reagem com gestos: festejam bolas, ficam desesperados com faltas e vingativos quando sabotados.</summary>
        void Bind(NetGame net)
        {
            _bound = net;
            net.ShotResolved += (shooter, foul, keep, won) =>
            {
                if (foul != FriendSlop.Billiards.FoulType.None) BotEmote(net, shooter, 2, 0.8f);
                else if (keep) BotEmote(net, shooter, Random.value < 0.5f ? 1 : 0, 0.5f);
            };
            net.RiskResolved += (id, outcome, def, eliminated) =>
            {
                if (outcome != FriendSlop.Core.RiskOutcome.Discharge) BotEmote(net, id, 1, 1f);
            };
            net.SabotageApplied += (buyer, target, def) =>
            {
                BotEmote(net, buyer, 0, 1f);
                BotEmote(net, target, 3, 1f);
            };
        }

        void BotEmote(NetGame net, int playerId, int emote, float chance)
        {
            if (Random.value > chance) return;
            var p = net.View.GetPlayer(playerId);
            if (p == null || !p.IsBot || p.Eliminated) return;
            ForSlot(p.SlotId)?.PlayEmote(emote);
        }

        public AvatarVisual ForSlot(int slotId) => _bySlot.TryGetValue(slotId, out var v) ? v : null;

        void Update()
        {
            var net = NetGame.Instance;
            if (net != null && net != _bound && net.IsSpawned) Bind(net);
            _seen.Clear();
            if (net != null && net.HasSnapshot)
            {
                var view = net.View;
                if (view.InMatch)
                {
                    int index = 0;
                    foreach (int id in view.BaseOrder)
                    {
                        var p = view.GetPlayer(id);
                        if (p != null && p.IsBot) UpdateMatchBot(net, view, p, index);
                        index++;
                    }
                }
                else
                {
                    // Na sala de espera os bots esperam à volta da mesa (longe das máquinas e adereços).
                    for (int i = 0; i < view.Slots.Count; i++)
                    {
                        var slot = view.Slots[i];
                        if (!slot.Profile.IsBot) continue;
                        var avatar = Ensure(slot.SlotId, slot.Profile);
                        var target = RoomBuilder.HomeSlot(i, Mathf.Max(4, view.Slots.Count));
                        Drive(avatar, target, new Vector3(0f, TableView.TopY, 0f), AvatarPose.Idle, false, -1, view);
                    }
                }
            }

            var stale = new List<int>();
            foreach (var kv in _bySlot)
                if (!_seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (int key in stale)
            {
                _velocity.Remove(_bySlot[key]);
                Destroy(_bySlot[key].transform.parent.gameObject);
                _bySlot.Remove(key);
            }
        }

        void UpdateMatchBot(NetGame net, MatchView view, PlayerView p, int index)
        {
            var avatar = Ensure(p.SlotId, p.Profile);
            avatar.SetGhost(p.Eliminated);
            avatar.Debuffed = p.Mods.Count > 0;

            Vector3 target = RoomBuilder.HomeSlot(index, view.BaseOrder.Count);
            Vector3 look = new Vector3(0f, TableView.TopY, 0f);
            var pose = AvatarPose.Idle;

            bool shooting = view.CurrentPlayer == p.Id &&
                            (view.Phase == MatchPhase.AwaitShot || view.Phase == MatchPhase.Simulating || view.Phase == MatchPhase.ShotResult);
            if (p.Eliminated)
            {
                // Fantasmas a flutuar por cima do sofá do lounge (longe da mesa e dos adereços).
                target = new Vector3(-0.3f + (p.SlotId % 4) * 0.6f, 0.9f, -RoomBuilder.HalfZ + 1.1f);
            }
            else if (shooting && net.Mirror.Cue != null)
            {
                Vector2 cue = net.Mirror.Cue.Position;
                Vector2 dir = net.RemoteAim.PlayerId == p.Id ? net.RemoteAim.Direction : Vector2.right;
                if (net.RemoteAim.PlayerId != p.Id)
                {
                    var lowest = net.Mirror.GetBall(net.Mirror.LowestBallOnTable());
                    if (lowest != null) dir = (lowest.Position - cue).normalized;
                }
                Vector2 stand = ShotMath.StandBehind(cue, dir, net.Mirror.Table);
                target = new Vector3(stand.x, 0f, stand.y);
                look = target + new Vector3(dir.x, 0f, dir.y) * 5f;
                if (view.Phase == MatchPhase.AwaitShot) pose = AvatarPose.Aiming;
            }
            else if ((view.Phase == MatchPhase.AwaitTrigger || view.Phase == MatchPhase.RiskReveal) && view.Selected == p.Id)
            {
                pose = AvatarPose.Risk;
            }
            else if ((view.Phase == MatchPhase.RoundWon && view.RoundWinner == p.Id) || (view.Phase == MatchPhase.MatchOver && view.MatchWinner == p.Id))
            {
                pose = AvatarPose.Celebrate;
            }

            // Só se debruça para a tacada quando já chegou ao sítio (não anda pela sala nessa pose).
            if (pose == AvatarPose.Aiming)
            {
                Vector3 at = avatar.transform.parent.position;
                if (new Vector2(at.x - target.x, at.z - target.z).sqrMagnitude > 0.3f * 0.3f) pose = AvatarPose.Idle;
            }
            Drive(avatar, target, look, pose, true, p.Id, view);
        }

        /// <summary>Para onde vale a pena olhar agora: a bola branca em jogo, quem vai à roleta, ou a máquina no sorteio.</summary>
        public static Vector3? ActionFocus(MatchView view, int self)
        {
            var net = NetGame.Instance;
            if (view == null || !view.InMatch || net == null) return null;
            switch (view.Phase)
            {
                case MatchPhase.AwaitShot:
                case MatchPhase.Simulating:
                case MatchPhase.ShotResult:
                    if (net.Mirror != null && net.Mirror.Cue != null) return TableView.ToWorld(net.Mirror.Cue.Position, 0.03f);
                    break;
                case MatchPhase.PunishmentDraw:
                    var game = GameBootstrap.Instance;
                    if (game != null && game.Randomizer != null) return game.Randomizer.transform.position + Vector3.up * 2.4f;
                    break;
                case MatchPhase.AwaitTrigger:
                case MatchPhase.RiskReveal:
                    if (view.Selected != self && AvatarLocator.TryHead(net, view.Selected, out var head)) return head;
                    break;
                case MatchPhase.RoundWon:
                    if (view.RoundWinner != self && AvatarLocator.TryHead(net, view.RoundWinner, out var winner)) return winner;
                    break;
            }
            return null;
        }

        /// <summary>
        /// Contorno da mesa: rectângulo da mesa alargado pela largura de um corpo. Se o segmento até ao alvo o
        /// atravessa, devolve o canto (visível daqui) que dá o caminho mais curto; senão o próprio alvo.
        /// </summary>
        static Vector3 AroundTable(Vector3 from, Vector3 to)
        {
            var game = GameBootstrap.Instance;
            if (game == null || game.Table == null) return to;
            // Margem menor que a de ShotMath.StandBehind (0,42) para o lugar de tacada ficar sempre fora do rectângulo.
            float ex = game.Table.Geometry.HalfLength + 0.3f, ez = game.Table.Geometry.HalfWidth + 0.3f;
            if (!SegmentHitsRect(from, to, ex, ez)) return to;
            float cx = ex + 0.25f, cz = ez + 0.25f;
            Vector3 best = to;
            float bestCost = float.MaxValue;
            foreach (var corner in new[] { new Vector3(cx, 0f, cz), new Vector3(-cx, 0f, cz), new Vector3(cx, 0f, -cz), new Vector3(-cx, 0f, -cz) })
            {
                if (SegmentHitsRect(from, corner, ex, ez)) continue;
                float cost = Vector3.Distance(from, corner) + Vector3.Distance(corner, to) + (SegmentHitsRect(corner, to, ex, ez) ? 3f : 0f);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = corner;
                }
            }
            return best;
        }

        /// <summary>Liang–Barsky no plano XZ contra o rectângulo |x|&lt;ex, |z|&lt;ez.</summary>
        static bool SegmentHitsRect(Vector3 a, Vector3 b, float ex, float ez)
        {
            float t0 = 0f, t1 = 1f;
            float dx = b.x - a.x, dz = b.z - a.z;
            float[] p = { -dx, dx, -dz, dz };
            float[] q = { a.x + ex, ex - a.x, a.z + ez, ez - a.z };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f)
                {
                    if (q[i] < 0f) return false;
                    continue;
                }
                float r = q[i] / p[i];
                if (p[i] < 0f) t0 = Mathf.Max(t0, r);
                else t1 = Mathf.Min(t1, r);
                if (t0 > t1) return false;
            }
            return true;
        }

        AvatarVisual Ensure(int slotId, FriendSlop.Core.PlayerProfile profile)
        {
            _seen.Add(slotId);
            if (!_bySlot.TryGetValue(slotId, out var avatar))
            {
                var holder = new GameObject("Bot_" + slotId);
                holder.transform.SetParent(transform, false);
                holder.transform.position = RoomBuilder.SpawnPoint(slotId + 3);
                // Os bots também são sólidos (os jogadores não os atravessam).
                var body = holder.AddComponent<CapsuleCollider>();
                body.height = 1.4f;
                body.radius = 0.26f;
                body.center = new Vector3(0f, 0.7f, 0f);
                avatar = AvatarVisual.Create(holder.transform, profile);
                var created = avatar;
                avatar.Footstep += at =>
                {
                    var g = GameBootstrap.Instance;
                    if (g != null && g.Sfx != null && created != null && created.transform.parent.position.y < 0.3f) g.Sfx.PlayStep(at, false);
                };
                _bySlot[slotId] = avatar;
            }
            avatar.Apply(profile);
            return avatar;
        }

        void Drive(AvatarVisual avatar, Vector3 target, Vector3 look, AvatarPose pose, bool inMatch, int playerId, MatchView view)
        {
            var holder = avatar.transform.parent;
            float dt = Time.deltaTime;
            Vector3 pos = holder.position;
            // Nunca em linha recta através da mesa: se o caminho a cruza, vai primeiro a um canto livre.
            Vector3 waypoint = new Vector3(target.x, pos.y, target.z);
            if (target.y < 0.2f) waypoint = AroundTable(pos, waypoint);
            waypoint.y = target.y;
            // Acelera e trava com suavidade e abranda ao chegar (sem arranques nem paragens secas).
            Vector3 toWaypoint = waypoint - pos;
            float dist = toWaypoint.magnitude;
            Vector3 desired = dist > 0.02f ? toWaypoint / dist * Mathf.Min(WalkSpeed, dist * 2.2f) : Vector3.zero;
            _velocity.TryGetValue(avatar, out var vel);
            vel = Vector3.MoveTowards(vel, desired, 7f * dt);
            _velocity[avatar] = vel;
            Vector3 next = pos + vel * dt;
            if ((next - pos).sqrMagnitude > toWaypoint.sqrMagnitude) next = waypoint;
            // Não empurra nem atravessa jogadores: afasta-se de quem estiver a menos de 0,6 m.
            foreach (var player in NetPlayer.All)
            {
                if (player == null || !player.IsSpawned || player.IsGhost) continue;
                Vector3 away = next - player.transform.position;
                away.y = 0f;
                float d = away.magnitude;
                if (d < 0.6f && d > 0.001f) next += away / d * (0.6f - d);
            }
            holder.position = next;

            Vector3 facing = (next - pos).sqrMagnitude > 1e-6f && (waypoint - next).sqrMagnitude > 0.01f ? next - pos : look - next;
            facing.y = 0f;
            if (facing.sqrMagnitude > 1e-4f)
                holder.rotation = Quaternion.Slerp(holder.rotation, Quaternion.LookRotation(facing), dt * 8f);

            var hud = GameBootstrap.Instance != null ? GameBootstrap.Instance.Hud : null;
            bool highlighted = inMatch && hud != null && hud.HighlightedPlayer == playerId;
            float pitch = pose == AvatarPose.Aiming ? 25f : 8f;
            // Os bots olham para a acção (bola branca, quem está na roleta), não sempre em frente.
            avatar.LookTarget = ActionFocus(view, playerId) ?? look;
            avatar.Animate(dt, pose, pitch, highlighted);
        }
    }

    /// <summary>Encontra o avatar de qualquer jogador da partida (humano ou bot).</summary>

    public static class AvatarLocator
    {
        public static AvatarVisual Find(NetGame net, int playerId)
        {
            if (net == null) return null;
            var p = net.View.GetPlayer(playerId);
            if (p == null) return null;
            if (p.IsBot) return NpcAvatars.Instance != null ? NpcAvatars.Instance.ForSlot(p.SlotId) : null;
            var player = NetPlayer.ForClient(p.ClientId);
            return player != null ? player.Visual : null;
        }

        public static bool TryHead(NetGame net, int playerId, out Vector3 head)
        {
            var avatar = Find(net, playerId);
            head = avatar != null && avatar.Head != null ? avatar.Head.position : Vector3.zero;
            return avatar != null;
        }
    }

    public static class ShotMath
    {
        /// <summary>Posição de pé atrás da branca, empurrada para fora da mesa.</summary>
        /// <summary>
        /// Lugar de quem joga: atrás da branca e ao LADO da linha do taco (à esquerda, jogadores destros), como nas
        /// referências — o taco passa por baixo do queixo e ao lado da anca direita, nunca através do corpo.
        /// Fica no primeiro ponto dessa linha que já está fora da mesa (nunca a menos de 0,75 m da bola).
        /// </summary>
        public const float StanceSide = 0.28f;

        public static Vector2 StandBehind(Vector2 cue, Vector2 aimDirection, FriendSlop.Billiards.TableGeometry table)
        {
            Vector2 d = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector2.right;
            float ex = table.HalfLength + 0.42f, ey = table.HalfWidth + 0.42f;
            float tx = Mathf.Abs(d.x) > 1e-4f ? (ex + Mathf.Sign(d.x) * cue.x) / Mathf.Abs(d.x) : float.MaxValue;
            float ty = Mathf.Abs(d.y) > 1e-4f ? (ey + Mathf.Sign(d.y) * cue.y) / Mathf.Abs(d.y) : float.MaxValue;
            float tExit = Mathf.Min(tx, ty);
            var left = new Vector2(-d.y, d.x);
            return cue - d * Mathf.Max(0.75f, tExit + 0.02f) + left * StanceSide;
        }

        /// <summary>
        /// Inclinação do taco (graus): o suficiente para passar por cima da tabela atrás da branca (bolas encostadas
        /// pedem o taco mais levantado), nunca menos de 4° nem mais de 16°.
        /// </summary>
        public static float CueTilt(Vector2 cue, Vector2 aimDirection, FriendSlop.Billiards.TableGeometry table, float ballRadius)
        {
            Vector2 d = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector2.right;
            float tx = Mathf.Abs(d.x) > 1e-4f ? (table.HalfLength + Mathf.Sign(d.x) * cue.x) / Mathf.Abs(d.x) : float.MaxValue;
            float ty = Mathf.Abs(d.y) > 1e-4f ? (table.HalfWidth + Mathf.Sign(d.y) * cue.y) / Mathf.Abs(d.y) : float.MaxValue;
            float toRail = Mathf.Max(0.03f, Mathf.Min(tx, ty));
            const float railClearance = 0.06f + 0.016f; // topo da tabela acima do pano + raio do taco
            float rise = railClearance - ballRadius;
            return Mathf.Clamp(Mathf.Atan2(rise, toRail) * Mathf.Rad2Deg, 4f, 16f);
        }

        /// <summary>Converte direcção no plano da mesa (x, z) para yaw de mundo (graus, convenção Unity).</summary>
        public static float WorldYaw(Vector2 tableDirection) => Mathf.Atan2(tableDirection.x, tableDirection.y) * Mathf.Rad2Deg;

        public static Vector2 TableDirection(float worldYaw)
        {
            float a = worldYaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }
    }
}
