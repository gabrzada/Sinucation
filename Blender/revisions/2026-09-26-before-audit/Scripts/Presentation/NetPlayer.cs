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
        public static readonly List<NetPlayer> All = new List<NetPlayer>();

        readonly NetworkVariable<float> _pitch = new NetworkVariable<float>(0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> _pose = new NetworkVariable<byte>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

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
        public float Pitch => IsOwner ? _pitchLocal : _pitch.Value;
        public Vector3 EyePosition => transform.position + Vector3.up * AvatarVisual.EyeHeight;
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
            _networkTransform = GetComponent<NetworkTransform>();
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
            _controller.radius = 0.28f;
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
            if (_controller != null) _controller.enabled = false;
            _yaw = yaw;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
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
                // Passos ao ritmo das pernas (não por distância).
                bool owner = IsOwner;
                _visual.Footstep += at =>
                {
                    var g = GameBootstrap.Instance;
                    if (g != null && g.Sfx != null && !_ghost) g.Sfx.PlayStep(at, owner);
                };
            }
            else _visual.Apply(profile);

            _ghost = ghost;
            if (_remoteBody != null) _remoteBody.enabled = !ghost;
            _visual.SetGhost(ghost);
            _visual.SetFirstPersonHidden(IsOwner);

            if (IsOwner) OwnerUpdate(game);

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
                ghost = player.Eliminated;
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
            if (!MovementLocked) transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (Mathf.Abs(_pitch.Value - _pitchLocal) > 0.5f) _pitch.Value = _pitchLocal;

            if (MovementLocked) return;

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
            if (move.sqrMagnitude > 1f) move.Normalize();

            if (_ghost)
            {
                if (_controller != null) _controller.enabled = false;
                var look = Quaternion.Euler(_pitchLocal, _yaw, 0f);
                Vector3 velocity = (look * new Vector3(move.x, 0f, move.y)) * (sprint ? FlySpeed * 1.8f : FlySpeed);
                if (jump) velocity.y += FlySpeed;
                if (down) velocity.y -= FlySpeed;
                transform.position = RoomBuilder.ClampInside(transform.position + velocity * dt, 0.2f, 3.2f);
                return;
            }

            if (_controller == null) return;
            if (!_controller.enabled) _controller.enabled = true;
            // Aceleração/travagem curtas: resposta rápida mas sem arranques secos (menos controlo no ar).
            Vector3 wanted = transform.rotation * new Vector3(move.x, 0f, move.y) * (sprint ? SprintSpeed : WalkSpeed);
            float accel = _controller.isGrounded ? (wanted.sqrMagnitude > 0.01f ? 22f : 28f) : 6f;
            _planarVelocity = Vector3.MoveTowards(_planarVelocity, wanted, accel * dt);
            Vector3 planar = _planarVelocity;
            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (jump) _verticalVelocity = JumpSpeed;
            }
            else
            {
                _verticalVelocity -= Gravity * dt;
            }
            _controller.Move((planar + Vector3.up * _verticalVelocity) * dt);
            if (transform.position.y < -2f) TeleportTo(RoomBuilder.SpawnPoint(0), _yaw);
        }
    }
}
