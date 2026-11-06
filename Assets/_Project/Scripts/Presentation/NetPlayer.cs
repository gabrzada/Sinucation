using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Personagem em primeira pessoa de cada cliente humano. O movimento é do dono (social physics — nunca
    /// toca nas bolas nem nas regras); o servidor só confia nele para apresentação. Eliminados e
    /// espectadores voam como fantasmas sem colisão (SPEC-001..003).
    /// </summary>
    public sealed class NetPlayer : NetworkBehaviour
    {
        /// <summary>Com 'Enter Play Mode Options' (sem recarregar o domínio) os estáticos sobrevivem entre Plays: repõe-os.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            Local = null;
            All.Clear();
        }

        // Bonecos pequenos (1,1 m): a 3 m/s as pernas curtas pareciam frenéticas.
        const float WalkSpeed = 2.7f;
        const float SprintSpeed = 4.5f;
        const float FlySpeed = 4.5f;
        const float JumpSpeed = 4.2f;
        const float Gravity = 14f;

        public static NetPlayer Local { get; private set; }
        /// <summary>QA (filmes do AutoPilot): mostra o corpo do jogador local mesmo em primeira pessoa.</summary>
        public static bool DebugShowSelf;
        /// <summary>QA (AutoPilot -fsWander): movimento simulado do jogador local, em vez do teclado.</summary>
        public static Vector2? DebugMove;
        public static bool DebugSprint;
        public static readonly List<NetPlayer> All = new List<NetPlayer>();

        readonly NetworkVariable<float> _pitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> _pose = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Velocidade de locomoção medida pelo dono (cm/s, XZ): a animação dos outros usa-a em vez da posição interpolada.</summary>
        readonly NetworkVariable<Vector2Int> _move = new NetworkVariable<Vector2Int>(Vector2Int.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        Vector3 _remoteMove;
        float _stunnedUntil = -10f;

        CharacterController _controller;
        CapsuleCollider _remoteBody;
        AvatarVisual _visual;
        NetworkTransform _networkTransform;
        float _yaw;
        float _pitchLocal;
        float _verticalVelocity;
        bool _ghost;
        Vector3 _planarVelocity;

        public AvatarVisual Visual => _visual;
        public float Yaw => _yaw;
        /// <summary>
        /// Para onde o corpo está virado, em qualquer cliente: o dono usa o seu olhar; nos remotos, <see cref="Yaw"/> nunca
        /// é actualizado (só o dono roda o rato) e a rotação vem do NetworkTransform.
        /// </summary>
        public float BodyYaw => IsOwner ? _yaw : transform.eulerAngles.y;
        public float Pitch => IsOwner ? _pitchLocal : _pitch.Value;
        public Vector3 EyePosition => transform.position + Vector3.up * AvatarVisual.EyeHeight;
        /// <summary>Caído depois de levar um tiro no modo de brincadeira (não anda até se levantar).</summary>
        public bool Stunned => Time.time < _stunnedUntil || (_visual != null && (_visual.InRagdoll || _visual.Recovering));
        public bool IsGhost => _ghost;
        public bool MovementLocked { get; set; }
        public bool LookLocked { get; set; }

        public AvatarPose Pose
        {
            get => (AvatarPose)_pose.Value;
            set
            {
                if (IsOwner && _pose.Value != (byte)value) _pose.Value = (byte)value;
            }
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            gameObject.layer = GameplayCollision.Player;
            _networkTransform = GetComponent<NetworkTransform>();
            // QA: -fsInterp 0|1|2 força o modo de interpolação (LegacyLerp, Lerp, SmoothDampening) para comparar.
            var args = System.Environment.GetCommandLineArgs();
            int ia = System.Array.IndexOf(args, "-fsInterp");
            if (_networkTransform != null && ia >= 0 && ia + 1 < args.Length && int.TryParse(args[ia + 1], out int mode))
            {
                _networkTransform.PositionInterpolationType = (NetworkTransform.InterpolationTypes)mode;
                _networkTransform.RotationInterpolationType = (NetworkTransform.InterpolationTypes)mode;
            }
            if (!IsOwner)
            {
                // Corpo sólido para os outros jogadores: o CharacterController local choca com ele.
                _remoteBody = gameObject.AddComponent<CapsuleCollider>();
                _remoteBody.height = 1.4f;
                _remoteBody.radius = 0.26f;
                _remoteBody.center = new Vector3(0f, 0.7f, 0f);
                return;
            }

            Local = this;
            _controller = gameObject.AddComponent<CharacterController>();
            _controller.height = 1.4f;
            _controller.radius = BodyRadius;
            _controller.center = new Vector3(0f, 0.7f, 0f);
            _controller.stepOffset = 0.3f;
            _controller.skinWidth = 0.03f;

            var spawn = RoomBuilder.SpawnPoint((int)(OwnerClientId % 8));
            Vector3 toTable = -spawn;
            TeleportTo(spawn, Mathf.Atan2(toTable.x, toTable.z) * Mathf.Rad2Deg);
            _pitchLocal = 12f;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            if (Local == this) Local = null;
        }

        public static NetPlayer ForClient(ulong clientId)
        {
            foreach (var p in All)
                if (p != null && p.IsSpawned && p.OwnerClientId == clientId) return p;
            return null;
        }

        /// <summary>Teletransporte feito pelo dono (autoridade do NetworkTransform).</summary>
        public void TeleportTo(Vector3 position, float yaw)
        {
            if (_controller != null) _controller.enabled = false;
            _yaw = yaw;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            if (_networkTransform != null && IsSpawned && IsOwner) _networkTransform.Teleport(position, rotation, transform.localScale);
            else transform.SetPositionAndRotation(position, rotation);
            if (_controller != null) _controller.enabled = !_ghost;
        }

        /// <summary>Usado pelo modo de tacada para mostrar o corpo inclinado atrás da branca.</summary>
        public void SetStance(Vector3 position, float yaw)
        {
            // On entering aim the controller is disabled and the body can be repositioned instantly.
            // Replant the visual rig even when that displacement is shorter than the network teleport threshold.
            bool entering = _controller != null && _controller.enabled;
            if (_visual != null && entering) _visual.ResetMotionForStance();
            if (_controller != null) _controller.enabled = false;
            _yaw = yaw;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            // Entrar na tacada ou mudar de lado da mesa move o corpo metros de uma vez: teletransporte de rede, para os
            // outros não verem o boneco deslizar (e atravessar a mesa) durante a interpolação.
            if (_networkTransform != null && IsSpawned && IsOwner && (position - transform.position).sqrMagnitude > 0.6f * 0.6f)
                _networkTransform.Teleport(position, rotation, transform.localScale);
            else transform.SetPositionAndRotation(position, rotation);
        }

        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitchLocal = Mathf.Clamp(pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        void Update()
        {
            var game = GameBootstrap.Instance;
            var net = NetGame.Instance;
            if (game == null) return;

            var profile = ResolveProfile(net, out bool ghost, out int playerId);
            if (_visual == null)
            {
                _visual = AvatarVisual.Create(transform, profile);
                // Levantou-se do ragdoll: o dono passa a estar onde o corpo caiu (replicado pelo NetworkTransform).
                _visual.RagdollEnded += at =>
                {
                    if (IsOwner && !_ghost && SafeGround(at, out var ground)) TeleportTo(ground, _yaw);
                };
                // Passos ao ritmo das pernas (não por distância).
                bool owner = IsOwner;
                _visual.Footstep += at =>
                {
                    var g = GameBootstrap.Instance;
                    if (g != null && g.Sfx != null && !_ghost) g.Sfx.PlayStep(at, owner);
                };
            }
            else _visual.Apply(profile);
            _visual.WaitForOwnerMove = !IsOwner;

            if (IsOwner && _ghost && !ghost)
            {
                // Deixou de ser fantasma (fim da partida → modo livre): o fantasma podia estar no ar ou dentro de um
                // adereço; volta a ter corpo num ponto de entrada da sala, virado para a mesa.
                var spawn = RoomBuilder.SpawnPoint((int)(OwnerClientId % 8));
                _ghost = false;
                TeleportTo(spawn, Mathf.Atan2(-spawn.x, -spawn.z) * Mathf.Rad2Deg);
            }
            _ghost = ghost;
            if (_remoteBody != null) _remoteBody.enabled = !ghost;
            _visual.SetGhost(ghost);
            bool cameraInsideBody = game.Rig == null || game.Rig.Camera == null ||
                Vector3.Distance(game.Rig.Camera.transform.position, _visual.Head.position) < 0.55f;
            // Na câmara de tacada o próprio corpo (cabeçorra e boné) ficava entre a câmara e a mesa: só a sombra fica.
            bool shotCamera = game.Shot != null && game.Shot.UsesShotCamera;
            // Caído com um tiro: a câmara vai com o corpo (ou fica, nos modos confortáveis) — o próprio corpo nunca aparece
            // à frente dos olhos nem entra e sai de vista enquanto cai e se levanta.
            bool fallen = !FunModeController.Legacy && (_visual.InRagdoll || _visual.Recovering);
            _visual.SetFirstPersonHidden(IsOwner && (cameraInsideBody || shotCamera || fallen) && !DebugShowSelf);

            if (IsOwner) OwnerUpdate(game);
            else
            {
                // Remotos: locomoção pela velocidade replicada (suavizada), não pelas pequenas paragens da interpolação.
                Vector3 target = new Vector3(_move.Value.x, 0f, _move.Value.y) * 0.01f;
                _remoteMove = Vector3.Lerp(_remoteMove, target, 1f - Mathf.Exp(-Time.deltaTime * 14f));
                _visual.ExternalVelocity = ghost ? (Vector3?)null : _remoteMove;
            }

            int highlighted = game.Hud != null ? game.Hud.HighlightedPlayer : -1;
            _visual.Debuffed = playerId >= 0 && net != null && net.View.GetPlayer(playerId) is var pv && pv != null && pv.Mods.Count > 0;
            _visual.Animate(Time.deltaTime, EffectivePose(net, playerId), Pitch, playerId >= 0 && playerId == highlighted);
        }

        /// <summary>A pose replicada (tacada) mais poses derivadas do estado da partida, iguais em todos os clientes.</summary>
        AvatarPose EffectivePose(NetGame net, int playerId)
        {
            if (net != null && net.HasSnapshot && net.View.InMatch && playerId >= 0)
            {
                var v = net.View;
                // Modo de brincadeira: o vencedor festeja um instante e depois fica de revólver na mão (braço por IK).
                if (v.Phase == FriendSlop.Match.MatchPhase.MatchOver) return v.MatchWinner == playerId ? AvatarPose.Celebrate : AvatarPose.Idle;
                if ((v.Phase == FriendSlop.Match.MatchPhase.AwaitTrigger || v.Phase == FriendSlop.Match.MatchPhase.RiskReveal) && v.Selected == playerId && !v.RiskEliminated)
                    return AvatarPose.Risk;
                if ((v.Phase == FriendSlop.Match.MatchPhase.RoundWon && v.RoundWinner == playerId) || (v.Phase == FriendSlop.Match.MatchPhase.MatchOver && v.MatchWinner == playerId))
                    return AvatarPose.Celebrate;
            }
            return Pose;
        }

        PlayerProfile ResolveProfile(NetGame net, out bool ghost, out int playerId)
        {
            ghost = false;
            playerId = -1;
            if (net == null || !net.HasSnapshot) return FallbackProfile;

            if (net.View.InMatch)
            {
                var player = net.View.PlayerForClient(OwnerClientId);
                if (player == null)
                {
                    ghost = true;
                    var slot = net.View.SlotForClient(OwnerClientId);
                    return slot != null ? slot.Profile : FallbackProfile;
                }
                playerId = player.Id;
                // No fim da partida toda a gente volta a ter corpo (modo de brincadeira).
                ghost = player.Eliminated && net.View.Phase != FriendSlop.Match.MatchPhase.MatchOver;
                return player.Profile;
            }

            var lobbySlot = net.View.SlotForClient(OwnerClientId);
            return lobbySlot != null ? lobbySlot.Profile : FallbackProfile;
        }

        static readonly PlayerProfile FallbackProfile = new PlayerProfile("…", Archetype.Circle, new Color(0.7f, 0.7f, 0.75f));

        void OwnerUpdate(GameBootstrap game)
        {
            float dt = Time.deltaTime;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool inputFree = !game.CursorFree && (game.Spectator == null || !game.Spectator.ControlsCamera);

            if (inputFree && !LookLocked && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue() * (0.075f * game.Settings.MouseSensitivity);
                _yaw += delta.x;
                _pitchLocal = Mathf.Clamp(_pitchLocal - delta.y, -85f, 85f);
            }
            if (!MovementLocked && !Stunned) transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (Mathf.Abs(_pitch.Value - _pitchLocal) > 0.5f) _pitch.Value = _pitchLocal;

            if (MovementLocked || Stunned)
            {
                _planarVelocity = Vector3.zero;
                PublishMove(Vector3.zero);
                return;
            }

            Vector2 move = Vector2.zero;
            bool sprint = false, jump = false, down = false;
            if (inputFree && keyboard != null)
            {
                if (keyboard.wKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed) move.x -= 1f;
                sprint = keyboard.leftShiftKey.isPressed;
                jump = keyboard.spaceKey.isPressed;
                down = keyboard.leftCtrlKey.isPressed;
            }
            if (DebugMove.HasValue)
            {
                move = DebugMove.Value;
                sprint = DebugSprint;
            }
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (_ghost)
            {
                if (_controller != null) _controller.enabled = false;
                var look = Quaternion.Euler(_pitchLocal, _yaw, 0f);
                Vector3 velocity = (look * new Vector3(move.x, 0f, move.y)) * (sprint ? FlySpeed * 1.8f : FlySpeed);
                if (jump) velocity.y += FlySpeed;
                if (down) velocity.y -= FlySpeed;
                transform.position = RoomBuilder.ClampInside(transform.position + velocity * dt, 0.2f, 3.2f);
                PublishMove(Vector3.zero);
                return;
            }

            if (_controller == null) return;
            if (!_controller.enabled) _controller.enabled = true;
            // Aceleração/travagem curtas: resposta rápida mas sem arranques secos (menos controlo no ar).
            Vector3 wanted = transform.rotation * new Vector3(move.x, 0f, move.y) * (sprint ? SprintSpeed : WalkSpeed);
            float accel = _controller.isGrounded ? (wanted.sqrMagnitude > 0.01f ? 22f : 28f) : 6f;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, wanted, accel * dt);
            Vector3 planar = _planarVelocity;
            // Quem está parado no lugar de tacada de um bot é afastado com suavidade (os bots desviam-se de pessoas e,
            // sem isto, ficavam à espera até ao fim do relógio sem conseguir tacar).
            if (NpcAvatars.ActiveBotStance is Vector3 stance)
            {
                Vector3 away = transform.position - stance;
                away.y = 0f;
                float d = away.magnitude;
                if (d < 0.75f) planar += (d > 0.01f ? away / d : transform.forward * -1f) * Mathf.Lerp(1.6f, 0.4f, d / 0.75f);
            }
            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (jump) _verticalVelocity = JumpSpeed;
            }
            else
            {
                _verticalVelocity -= Gravity * dt;
            }
            // Regra da sala: com "subir à mesa" ligado a barreira sai e quem está em cima do tampo fica lá.
            var room = NetGame.Instance;
            bool climb = room != null && room.HasSnapshot && room.View.Settings.AllowTableClimb;
            RoomBuilder.SetTableClimbAllowed(climb);
            _controller.Move((planar + Vector3.up * _verticalVelocity) * dt);
            bool onTable = climb && transform.position.y > TableView.TopY - 0.12f;
            Vector3 outside = onTable ? transform.position : OutsideTable(transform.position);
            if ((outside - transform.position).sqrMagnitude > 0.000001f)
            {
                _controller.enabled = false;
                transform.position = outside;
                _controller.enabled = true;
            }
            PublishMove(_controller.velocity);
            if (transform.position.y < -2f) TeleportTo(RoomBuilder.SpawnPoint(0), _yaw);
        }

        void PublishMove(Vector3 velocity)
        {
            var v = new Vector2Int(Mathf.RoundToInt(velocity.x * 100f), Mathf.RoundToInt(velocity.z * 100f));
            // Só quando muda o suficiente (8 cm/s): poucas mensagens, sem tremer.
            if (Mathf.Abs(v.x - _move.Value.x) > 8 || Mathf.Abs(v.y - _move.Value.y) > 8 || (v == Vector2Int.zero && _move.Value != Vector2Int.zero))
                _move.Value = v;
        }

        /// <summary>
        /// Onde o corpo caído pode voltar a ficar de pé: dentro da sala, fora da mesa e sem se sobrepor a nada
        /// (senão fica onde estava — nunca preso dentro de um móvel).
        /// </summary>
        bool SafeGround(Vector3 at, out Vector3 ground)
        {
            float y = transform.position.y;
            ground = OutsideTable(RoomBuilder.ClampInside(new Vector3(at.x, y, at.z), y, y));
            if ((ground - transform.position).sqrMagnitude < 0.0025f) return false;
            var mask = ~0;
            bool blocked = Physics.CheckCapsule(ground + Vector3.up * 0.35f, ground + Vector3.up * 1.05f, 0.27f, mask, QueryTriggerInteraction.Ignore) &&
                           OverlapsOther(ground);
            return !blocked;
        }

        public const float BodyRadius = 0.28f;

        static Vector3 OutsideTable(Vector3 p)
        {
            var physics = GameBootstrap.Instance != null ? GameBootstrap.Instance.Config.Physics : null;
            return physics == null ? p : RoomBuilder.OutsideTable(p, physics.TableLength * 0.5f, physics.TableWidth * 0.5f, BodyRadius);
        }

        /// <summary>
        /// Depois da tacada a personagem volta a ter física no lugar de tacada, que fica colado à mesa: sai para fora do
        /// collider antes de reactivar o CharacterController (sem "saltos" de despenetração).
        /// </summary>
        public void SettleOutsideTable(float yaw)
        {
            Vector3 p = OutsideTable(transform.position);
            TeleportTo(p, yaw);
        }

        bool OverlapsOther(Vector3 ground)
        {
            foreach (var c in Physics.OverlapCapsule(ground + Vector3.up * 0.35f, ground + Vector3.up * 1.05f, 0.27f, ~0, QueryTriggerInteraction.Ignore))
                if (c != null && !c.transform.IsChildOf(transform) && c.attachedRigidbody == null) return true;
            return false;
        }

        /// <summary>Levou um tiro no modo de brincadeira: cai em ragdoll e só volta a andar quando se levanta.</summary>
        public void Stun(float seconds) => _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + seconds);
    }
}
