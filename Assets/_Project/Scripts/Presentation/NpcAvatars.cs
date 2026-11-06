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
            ActiveBotStance = null;
        }

        const float WalkSpeed = 1.7f;

        public static NpcAvatars Instance { get; private set; }

        /// <summary>Lugar de tacada (mundo) do bot que está a jogar agora, para os jogadores lhe darem espaço.</summary>
        public static Vector3? ActiveBotStance { get; private set; }

        readonly Dictionary<int, AvatarVisual> _bySlot = new Dictionary<int, AvatarVisual>();
        readonly HashSet<int> _seen = new HashSet<int>();
        readonly Dictionary<AvatarVisual, Vector3> _velocity = new Dictionary<AvatarVisual, Vector3>();
        // Reutilizados em cada frame (sem lixo para o GC: menos micro-travamentos).
        readonly List<int> _stale = new List<int>();
        static readonly Vector3[] s_corners = new Vector3[4];

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
            if (p == null || p.Eliminated) return;
            // Bots e humanos reagem igual (festejo, frustração, vingança); é só apresentação local, a partir do evento.
            AvatarLocator.Find(net, playerId)?.PlayEmote(emote);
        }

        public AvatarVisual ForSlot(int slotId) => _bySlot.TryGetValue(slotId, out var v) ? v : null;

        public bool ReadyToShoot(NetGame net, int id)
        {
            var player = net.View.GetPlayer(id);
            var avatar = player != null ? ForSlot(player.SlotId) : null;
            if (avatar == null || net.Mirror.Cue == null || net.RemoteAim.PlayerId != id) return false;
            var dir = net.RemoteAim.Direction;
            var target = ShotMath.StandBehind(net.Mirror.Cue.Position, dir, net.Mirror.Table);
            var at = avatar.transform.parent.position;
            // Tolerância igual à da pose de mira (0,3 m): assim que se debruça sobre a mesa pode começar a preparar a tacada.
            return Vector2.Distance(new Vector2(at.x, at.z), target) < 0.25f &&
                Mathf.Abs(Mathf.DeltaAngle(avatar.transform.parent.eulerAngles.y, ShotMath.WorldYaw(dir))) < 20f;
        }

        void Update()
        {
            var net = NetGame.Instance;
            if (net != null && net != _bound && net.IsSpawned) Bind(net);
            _seen.Clear();
            ActiveBotStance = null;
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

            _stale.Clear();
            foreach (var kv in _bySlot)
                if (!_seen.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (int key in _stale)
            {
                _velocity.Remove(_bySlot[key]);
                _restAt.Remove(_bySlot[key]);
                Destroy(_bySlot[key].transform.parent.gameObject);
                _bySlot.Remove(key);
            }
        }

        void UpdateMatchBot(NetGame net, MatchView view, PlayerView p, int index)
        {
            var avatar = Ensure(p.SlotId, p.Profile);
            bool over = view.Phase == MatchPhase.MatchOver;
            // Fim da partida: toda a gente volta a ter corpo e a estar na sala (modo de brincadeira).
            avatar.SetGhost(p.Eliminated && !over);
            avatar.Debuffed = p.Mods.Count > 0 && !over;

            Vector3 target = RoomBuilder.HomeSlot(index, view.BaseOrder.Count);
            Vector3 look = new Vector3(0f, TableView.TopY, 0f);
            var pose = AvatarPose.Idle;
            if (over)
            {
                if (view.MatchWinner == p.Id) pose = AvatarPose.Celebrate;
                // Depois de levantar-se de um tiro, fica onde caiu; os bots não fogem (é só para rir).
                if (_restAt.TryGetValue(avatar, out var rest)) target = rest;
                Drive(avatar, target, look, pose, true, p.Id, view);
                return;
            }

            bool shooting = view.CurrentPlayer == p.Id &&
                            (view.Phase == MatchPhase.AwaitShot || view.Phase == MatchPhase.ShotWindup || view.Phase == MatchPhase.Simulating || view.Phase == MatchPhase.ShotResult);
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
                if (view.Phase == MatchPhase.AwaitShot) ActiveBotStance = target;
                if (view.Phase == MatchPhase.AwaitShot || view.Phase == MatchPhase.ShotWindup) pose = AvatarPose.Aiming;
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
                case MatchPhase.ShotWindup:
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
            // Collider da mesa + raio do bot, um pouco menos do que a margem do lugar de tacada (0,42) para esse lugar
            // continuar fora do rectângulo de contorno.
            float ex = game.Table.Geometry.HalfLength + 0.4f, ez = game.Table.Geometry.HalfWidth + 0.4f;
            if (!SegmentHitsRect(from, to, ex, ez)) return to;
            float cx = ex + 0.25f, cz = ez + 0.25f;
            Vector3 best = to;
            float bestCost = float.MaxValue;
            s_corners[0] = new Vector3(cx, 0f, cz);
            s_corners[1] = new Vector3(-cx, 0f, cz);
            s_corners[2] = new Vector3(cx, 0f, -cz);
            s_corners[3] = new Vector3(-cx, 0f, -cz);
            foreach (var corner in s_corners)
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
            for (int i = 0; i < 4; i++)
            {
                float pi = i == 0 ? -dx : i == 1 ? dx : i == 2 ? -dz : dz;
                float qi = i == 0 ? a.x + ex : i == 1 ? ex - a.x : i == 2 ? a.z + ez : ez - a.z;
                if (Mathf.Abs(pi) < 1e-6f)
                {
                    if (qi < 0f) return false;
                    continue;
                }
                float r = qi / pi;
                if (pi < 0f) t0 = Mathf.Max(t0, r);
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
                // Levantou-se de um tiro (modo de brincadeira): o suporte passa para onde o corpo caiu.
                avatar.RagdollEnded += at =>
                {
                    if (created == null) return;
                    var h = created.transform.parent;
                    Vector3 p = RoomBuilder.ClampInside(new Vector3(at.x, h.position.y, at.z), h.position.y, h.position.y);
                    float ex = 1.12f + 0.45f, ez = 0.56f + 0.45f;
                    if (Mathf.Abs(p.x) < ex && Mathf.Abs(p.z) < ez)
                    {
                        if (ex - Mathf.Abs(p.x) < ez - Mathf.Abs(p.z)) p.x = Mathf.Sign(p.x == 0f ? 1f : p.x) * ex;
                        else p.z = Mathf.Sign(p.z == 0f ? 1f : p.z) * ez;
                    }
                    h.position = p;
                    _restAt[created] = p;
                };
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

        readonly Dictionary<AvatarVisual, Vector3> _restAt = new Dictionary<AvatarVisual, Vector3>();

        void Drive(AvatarVisual avatar, Vector3 target, Vector3 look, AvatarPose pose, bool inMatch, int playerId, MatchView view)
        {
            var holder = avatar.transform.parent;
            float dt = Time.deltaTime;
            if (view == null || !view.InMatch || view.Phase != MatchPhase.MatchOver) _restAt.Remove(avatar);
            // Em ragdoll (ou a levantar-se) o corpo é da física: o suporte não anda.
            if (avatar.InRagdoll || avatar.Recovering)
            {
                avatar.Animate(dt, pose, 8f, false);
                return;
            }
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
            // Não empurra nem atravessa jogadores: afasta-se de quem estiver a menos de 0,6 m (a caminho do lugar de
            // tacada só 0,35 m — quem lá está vai sendo afastado, ver NetPlayer).
            float personal = ActiveBotStance.HasValue && (ActiveBotStance.Value - target).sqrMagnitude < 0.01f ? 0.35f : 0.6f;
            foreach (var player in NetPlayer.All)
            {
                if (player == null || !player.IsSpawned || player.IsGhost) continue;
                Vector3 away = next - player.transform.position;
                away.y = 0f;
                float d = away.magnitude;
                if (d < personal && d > 0.001f) next += away / d * (personal - d);
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
            // Apply the lateral stance before finding the exit. Applying it afterwards can
            // move diagonal targets inside the rectangle that bot navigation must avoid.
            var left = new Vector2(-d.y, d.x);
            Vector2 origin = cue + left * StanceSide;
            // Match the clearance used when the CharacterController is re-enabled after a shot.
            const float clearance = RoomBuilder.TableColliderMargin + NetPlayer.BodyRadius + 0.02f;
            float ex = table.HalfLength + clearance, ey = table.HalfWidth + clearance;
            float tx = Mathf.Abs(d.x) > 1e-4f ? (ex + Mathf.Sign(d.x) * origin.x) / Mathf.Abs(d.x) : float.MaxValue;
            float ty = Mathf.Abs(d.y) > 1e-4f ? (ey + Mathf.Sign(d.y) * origin.y) / Mathf.Abs(d.y) : float.MaxValue;
            float tExit = Mathf.Min(tx, ty);
            return origin - d * Mathf.Max(0.75f, tExit + 0.02f);
        }

        /// <summary>
        /// Inclinação do taco (graus): o suficiente para passar por cima da tabela atrás da branca (bolas encostadas
        /// pedem o taco mais levantado), com um mínimo de 1° e um máximo de 16°.
        /// </summary>
        public static float CueTilt(Vector2 cue, Vector2 aimDirection, FriendSlop.Billiards.TableGeometry table, float ballRadius)
        {
            Vector2 d = aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector2.right;
            float tx = Mathf.Abs(d.x) > 1e-4f ? (table.HalfLength + Mathf.Sign(d.x) * cue.x) / Mathf.Abs(d.x) : float.MaxValue;
            float ty = Mathf.Abs(d.y) > 1e-4f ? (table.HalfWidth + Mathf.Sign(d.y) * cue.y) / Mathf.Abs(d.y) : float.MaxValue;
            float toRail = Mathf.Max(0.03f, Mathf.Min(tx, ty));
            const float railClearance = 0.06f + 0.016f; // topo da tabela acima do pano + raio do taco
            float rise = railClearance - ballRadius;
            return Mathf.Clamp(Mathf.Atan2(rise, toRail) * Mathf.Rad2Deg, 1f, 16f);
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


