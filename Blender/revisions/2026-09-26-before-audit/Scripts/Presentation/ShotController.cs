using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    public enum ShotState
    {
        None,
        Placing,
        Aiming,
        Watching
    }

    /// <summary>
    /// Tacada em primeira pessoa. [E] pega no taco: a câmara desce atrás da branca e o rato roda a mira.
    /// Segurar o clique e puxar o rato para trás recua o taco; soltar dá a tacada (força = recuo).
    /// Só produz intenções (ShotRequest/PlaceCue); o servidor valida e simula.
    /// </summary>
    public sealed class ShotController : MonoBehaviour
    {
        public const float MaxPull = 0.3f;
        const float PullPerPixel = 0.0011f;
        const float AimSendInterval = 1f / 15f;

        GameBootstrap _game;
        float _worldYaw;
        float _pitch = 14f;
        float _distance = 0.62f;
        float _pull;
        float _aimTimer;
        float _shotTime;
        int _shotSentSequence = -1;
        Vector3 _watchPosition;
        Quaternion _watchRotation;

        public ShotState State { get; private set; }
        public bool UsesShotCamera => State == ShotState.Aiming || State == ShotState.Watching;
        public bool TopView { get; private set; }
        public bool Pulling { get; private set; }
        public bool CanTakeShot { get; private set; }
        public float Pull01 => Mathf.Clamp01(_pull / MaxPull);
        public float PullDistance => _pull;
        public Vector2 Spin { get; private set; }
        public Vector2 AimDirection { get; private set; } = Vector2.right;
        public Vector3 CameraPosition { get; private set; }
        public Quaternion CameraRotation { get; private set; }
        public bool PlacementVisible { get; private set; }
        public bool PlacementValid { get; private set; }
        public Vector2 PlacementPoint { get; private set; }

        public void Initialize(GameBootstrap game) => _game = game;

        /// <summary>Usado pelo AutoPilot (testes automáticos): põe a câmara de tacada numa mira concreta.</summary>
        public void DebugAim(Vector2 tableDirection, float pull01)
        {
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            if (net == null || local == null) return;
            State = ShotState.Aiming;
            InitAim(net, local, true);
            _worldYaw = ShotMath.WorldYaw(tableDirection);
            _pitch = 16f;
            _pull = Mathf.Clamp01(pull01) * MaxPull;
        }

        public void SetSpin(Vector2 spin)
        {
            if (spin.sqrMagnitude > 1f) spin.Normalize();
            Spin = spin;
        }

        void Update()
        {
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            CanTakeShot = false;
            PlacementVisible = false;

            if (net == null || !net.HasSnapshot || local == null || _game.Paused)
            {
                if (State != ShotState.None && (net == null || local == null)) Exit(local);
                return;
            }

            var view = net.View;
            var me = net.LocalPlayer;
            bool mine = view.InMatch && me != null && !me.Eliminated && view.CurrentPlayer == me.Id;
            bool shotPhase = mine && view.Phase == MatchPhase.AwaitShot;
            bool watchPhase = mine && (view.Phase == MatchPhase.Simulating || view.Phase == MatchPhase.ShotResult);

            if (watchPhase)
            {
                if (State == ShotState.Aiming) BeginWatching();
                if (State == ShotState.Placing) Exit(local);
                if (State == ShotState.Watching)
                {
                    _pull = Mathf.MoveTowards(_pull, 0f, Time.deltaTime * 3f);
                    CameraPosition = _watchPosition;
                    CameraRotation = _watchRotation;
                }
                return;
            }

            if (!shotPhase)
            {
                if (State != ShotState.None) Exit(local);
                return;
            }

            if (State == ShotState.Watching)
            {
                bool waitingServer = view.ShotSequence == _shotSentSequence && Time.time - _shotTime < 2f;
                if (waitingServer)
                {
                    CameraPosition = _watchPosition;
                    CameraRotation = _watchRotation;
                    return;
                }
                State = ShotState.Aiming;
                InitAim(net, local, true);
            }

            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            bool inputFree = !_game.CursorFree;

            if (State == ShotState.None)
            {
                CanTakeShot = true;
                bool interacting = _game.Interaction != null && (_game.Interaction.Focused != null || _game.Interaction.Active != null);
                if (inputFree && !interacting && keyboard != null && keyboard.eKey.wasPressedThisFrame) Enter(net, local);
                return;
            }

            if (inputFree && keyboard != null && keyboard.qKey.wasPressedThisFrame)
            {
                Exit(local);
                return;
            }

            if (State == ShotState.Placing) UpdatePlacing(net, local, keyboard, mouse, inputFree);
            else UpdateAiming(net, local, keyboard, mouse, inputFree);
        }

        void Enter(NetGame net, NetPlayer local)
        {
            Spin = Vector2.zero;
            if (net.View.BallInHand)
            {
                State = ShotState.Placing;
                return;
            }
            State = ShotState.Aiming;
            InitAim(net, local, false);
        }

        void InitAim(NetGame net, NetPlayer local, bool keepYaw)
        {
            _pull = 0f;
            Pulling = false;
            if (!keepYaw && net.Mirror.Cue != null)
            {
                Vector3 cue = TableView.ToWorld(net.Mirror.Cue.Position, 0f);
                Vector3 fromPlayer = cue - local.transform.position;
                fromPlayer.y = 0f;
                _worldYaw = fromPlayer.sqrMagnitude > 0.01f ? Mathf.Atan2(fromPlayer.x, fromPlayer.z) * Mathf.Rad2Deg : local.Yaw;
                _pitch = 14f;
            }
            local.MovementLocked = true;
            local.LookLocked = true;
        }

        void BeginWatching()
        {
            State = ShotState.Watching;
            Pulling = false;
            TopView = false;
            _watchPosition = CameraPosition;
            _watchRotation = CameraRotation;
        }

        void Exit(NetPlayer local)
        {
            State = ShotState.None;
            Pulling = false;
            TopView = false;
            _pull = 0f;
            if (local == null) return;
            local.MovementLocked = false;
            local.LookLocked = false;
            local.Pose = AvatarPose.Idle;
            local.SetLook(_worldYaw, 22f);
            local.TeleportTo(local.transform.position, _worldYaw);
        }

        void UpdatePlacing(NetGame net, NetPlayer local, Keyboard keyboard, Mouse mouse, bool inputFree)
        {
            local.MovementLocked = false;
            local.LookLocked = false;
            local.Pose = AvatarPose.Idle;

            if (inputFree && keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                State = ShotState.Aiming;
                InitAim(net, local, false);
                return;
            }

            var cam = _game.Rig.Camera;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            float r = net.Mirror.Settings.BallRadius;
            var plane = new Plane(Vector3.up, new Vector3(0f, TableView.TopY + r, 0f));
            if (!plane.Raycast(ray, out float enter) || enter > 6f) return;

            var table = net.Mirror.Table;
            Vector2 p = TableView.ToTable(ray.GetPoint(enter));
            if (Mathf.Abs(p.x) > table.HalfLength + 0.3f || Mathf.Abs(p.y) > table.HalfWidth + 0.3f) return;
            p.x = Mathf.Clamp(p.x, -table.HalfLength + r, net.View.KitchenOnly ? table.HeadStringX : table.HalfLength - r);
            p.y = Mathf.Clamp(p.y, -table.HalfWidth + r, table.HalfWidth - r);

            PlacementVisible = true;
            PlacementPoint = p;
            PlacementValid = net.Mirror.IsValidCuePlacement(p, net.View.KitchenOnly);
            SendAim(net, new AimState { PlayerId = net.LocalPlayer.Id, Placing = true, PlacePosition = p });

            if (inputFree && mouse != null && mouse.leftButton.wasPressedThisFrame && PlacementValid)
            {
                net.RequestPlaceCue(p);
                State = ShotState.Aiming;
                Vector3 toCue = TableView.ToWorld(p, 0f) - local.transform.position;
                toCue.y = 0f;
                InitAim(net, local, true);
                if (toCue.sqrMagnitude > 0.01f) _worldYaw = Mathf.Atan2(toCue.x, toCue.z) * Mathf.Rad2Deg;
            }
        }

        void UpdateAiming(NetGame net, NetPlayer local, Keyboard keyboard, Mouse mouse, bool inputFree)
        {
            local.MovementLocked = true;
            local.LookLocked = true;
            local.Pose = AvatarPose.Aiming;
            float dt = Time.deltaTime;

            if (inputFree && keyboard != null && net.View.BallInHand && keyboard.bKey.wasPressedThisFrame)
            {
                State = ShotState.Placing;
                Pulling = false;
                return;
            }

            TopView = inputFree && keyboard != null && keyboard.tabKey.isPressed;
            Vector2 raw = inputFree && mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            float sensitivity = 0.075f * _game.Settings.MouseSensitivity;
            float fine = keyboard != null && keyboard.leftShiftKey.isPressed ? 0.18f : 1f;

            if (!Pulling)
            {
                _worldYaw += raw.x * sensitivity * fine;
                _pitch = Mathf.Clamp(_pitch - raw.y * sensitivity * fine, 3f, 70f);
            }
            if (inputFree && mouse != null)
            {
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _distance = Mathf.Clamp(_distance - Mathf.Sign(scroll) * 0.06f, 0.3f, 1.5f);
            }

            if (keyboard != null && inputFree)
            {
                Vector2 spin = Spin;
                const float rate = 1.6f;
                if (keyboard.upArrowKey.isPressed) spin.y += rate * dt;
                if (keyboard.downArrowKey.isPressed) spin.y -= rate * dt;
                if (keyboard.rightArrowKey.isPressed) spin.x += rate * dt;
                if (keyboard.leftArrowKey.isPressed) spin.x -= rate * dt;
                if (keyboard.cKey.wasPressedThisFrame) spin = Vector2.zero;
                SetSpin(spin);
            }

            if (inputFree && mouse != null && !Pulling && mouse.leftButton.wasPressedThisFrame)
            {
                Pulling = true;
                _pull = 0f;
            }
            if (Pulling && mouse != null)
            {
                if (mouse.rightButton.wasPressedThisFrame)
                {
                    Pulling = false;
                    _pull = 0f;
                }
                else
                {
                    _pull = Mathf.Clamp(_pull - raw.y * PullPerPixel * fine, 0f, MaxPull);
                    if (!mouse.leftButton.isPressed)
                    {
                        Pulling = false;
                        if (_pull > 0.006f) Shoot(net);
                        else _pull = 0f;
                    }
                }
            }

            var profile = FirstPersonRig.LocalShooterProfile(net, _game.Settings.ComfortMode);
            float sway = profile.AimSway > 0f ? (Mathf.PerlinNoise(Time.time * 0.6f, 3.3f) * 2f - 1f) * 7f * profile.AimSway : 0f;
            AimDirection = ShotMath.TableDirection(_worldYaw + sway);

            UpdateCameraAndStance(net, local);
            SendAim(net, new AimState
            {
                PlayerId = net.LocalPlayer.Id,
                Yaw = Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg,
                Pull = Pull01,
                Spin = Spin
            });
        }

        void UpdateCameraAndStance(NetGame net, NetPlayer local)
        {
            if (net.Mirror.Cue == null) return;
            float r = net.Mirror.Settings.BallRadius;
            Vector2 cue = net.Mirror.Cue.Position;
            Vector3 cueWorld = TableView.ToWorld(cue, r);
            var dir3 = new Vector3(AimDirection.x, 0f, AimDirection.y);

            if (TopView)
            {
                CameraPosition = new Vector3(cueWorld.x * 0.5f, TableView.TopY + 2.3f, cueWorld.z * 0.5f);
                CameraRotation = Quaternion.LookRotation(Vector3.down, dir3);
            }
            else
            {
                float pitch = _pitch * Mathf.Deg2Rad;
                float back = _distance * Mathf.Cos(pitch) + _pull * 0.35f;
                CameraPosition = cueWorld - dir3 * back + Vector3.up * (_distance * Mathf.Sin(pitch) + 0.03f);
                Vector3 look = cueWorld + dir3 * 0.3f;
                CameraRotation = Quaternion.LookRotation(look - CameraPosition);
            }

            Vector2 stand = ShotMath.StandBehind(cue, AimDirection, net.Mirror.Table);
            local.SetStance(new Vector3(stand.x, 0f, stand.y), ShotMath.WorldYaw(AimDirection));
        }

        void Shoot(NetGame net)
        {
            _shotSentSequence = net.View.ShotSequence;
            _shotTime = Time.time;
            net.RequestShot(AimDirection, Pull01, Spin);
            BeginWatching();
        }

        void SendAim(NetGame net, AimState aim)
        {
            _aimTimer += Time.deltaTime;
            if (_aimTimer < AimSendInterval) return;
            _aimTimer = 0f;
            net.SendAim(aim);
        }
    }
}
