using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    public enum ShotState
    {
        None,
        /// <summary>Bola na mão: a apontar para a mesa com a pré-visualização da branca.</summary>
        Placing,
        /// <summary>Bola na mão enviada ao anfitrião, à espera da confirmação.</summary>
        PlacePending,
        Aiming,
        /// <summary>Acompanhamento da tacada (o taco atravessa a branca) — câmara de tacada durante meio segundo.</summary>
        Watching,
        /// <summary>Bolas em movimento: a personagem anda e a câmara é livre; nova tacada só no próximo turno.</summary>
        Observing
    }

    /// <summary>
    /// Tacada em primeira pessoa. [E] pega no taco: a câmara desce atrás da branca e o rato roda a mira.
    /// Segurar o clique e puxar o rato para trás recua o taco; soltar dá a tacada (força = recuo).
    /// Depois da tacada a câmara volta à primeira pessoa e o jogador pode andar e olhar enquanto as bolas rolam
    /// (o bloqueio de nova tacada é da autoridade, não da câmara). Só produz intenções (ShotRequest/PlaceCue).
    /// </summary>
    public sealed class ShotController : MonoBehaviour
    {
        public const float MaxPull = 0.3f;
        const float PullPerPixel = 0.0011f;
        const float AimSendInterval = 1f / 15f;
        /// <summary>Tempo de câmara de tacada depois de soltar (o taco atravessa a branca e a bola parte).</summary>
        const float FollowThroughSeconds = 0.55f;
        const float PlaceTimeout = 1.6f;
        /// <summary>Se o jogador ficou no lugar da tacada, a mira volta sozinha quando é outra vez a vez dele.</summary>
        const float ResumeAimRadius = 0.6f;

        GameBootstrap _game;
        NetGame _boundNet;
        float _worldYaw;
        float _pitch = 14f;
        float _distance = 0.62f;
        float _pull;
        float _lastPower = 0.55f;
        float _aimTimer;
        float _shotTime;
        int _shotSentSequence = -1;
        Vector3 _watchPosition;
        Quaternion _watchRotation;
        Vector3 _observeFrom;
        bool _resumeAim;
        int _placeRequest = -1;
        float _placeSentAt;
        Vector2 _placeSentAt2;
        Vector2? _cueOverride;
        float _cueOverrideUntil;
        readonly List<ModifierInstance> _mods = new List<ModifierInstance>();

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
        public bool PlacementPending => State == ShotState.PlacePending;
        public Vector2 PlacementPoint { get; private set; }
        /// <summary>Porque é que o sítio apontado não serve (null se serve ou se não está a apontar para a mesa).</summary>
        public string PlacementHint { get; private set; }
        /// <summary>Última recusa do anfitrião (bola na mão) e quando chegou, para a HUD.</summary>
        public string LastRefusal { get; private set; }
        public float LastRefusalTime { get; private set; } = -10f;
        /// <summary>Força usada na previsão da trajectória: a do recuo actual, ou a da última tacada.</summary>
        public float PreviewPower => _pull > 0.004f ? Pull01 : _lastPower;

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

        /// <summary>AutoPilot: percorre o fluxo real da bola na mão (pré-visualização, validação e confirmação).</summary>
        public bool DebugPlace(Vector2 tablePoint)
        {
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            if (net == null || local == null || !net.View.BallInHand) return false;
            if (State == ShotState.None) Enter(net, local);
            if (State != ShotState.Placing) return false;
            var sim = net.Mirror;
            if (!sim.TryFindPlacementNear(tablePoint, net.View.KitchenOnly, sim.Settings.BallRadius * 3f, out var p)) return false;
            PlacementPoint = p;
            PlacementValid = true;
            SendPlacement(net, p);
            return true;
        }

        public void SetSpin(Vector2 spin)
        {
            if (spin.sqrMagnitude > 1f) spin.Normalize();
            Spin = spin;
        }

        /// <summary>Posição da branca para a câmara e o taco: a acabada de pousar enquanto o snapshot não chega.</summary>
        public Vector2 CuePosition(NetGame net)
        {
            var cue = net.Mirror.Cue;
            Vector2 mirror = cue != null ? cue.Position : Vector2.zero;
            if (_cueOverride.HasValue)
            {
                if (Time.time > _cueOverrideUntil || (mirror - _cueOverride.Value).sqrMagnitude < 1e-6f) _cueOverride = null;
                else return _cueOverride.Value;
            }
            return mirror;
        }

        /// <summary>Parâmetros efectivos da tacada que o anfitrião vai simular (mesmo pipeline de modificadores).</summary>
        public ShotParameters PreviewShot(NetGame net)
        {
            var shot = ShotParameters.FromInput(AimDirection, PreviewPower, Spin.x, Spin.y);
            _mods.Clear();
            var me = net.LocalPlayer;
            var catalog = NetGame.SharedCatalog;
            if (me != null && catalog != null)
                for (int i = 0; i < me.Mods.Count; i++)
                {
                    var m = me.Mods[i];
                    if (m.Def >= 0 && m.Def < catalog.Count)
                        _mods.Add(new ModifierInstance { Definition = catalog[m.Def], Intensity = Mathf.Max(1, m.Intensity), InstanceId = i });
                }
            return ShotModifierPipeline.Apply(shot, _mods);
        }

        void Update()
        {
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            CanTakeShot = false;
            PlacementVisible = false;
            PlacementHint = null;
            if (net != _boundNet)
            {
                if (_boundNet != null) _boundNet.PlaceCueResult -= OnPlaceResult;
                _boundNet = net;
                if (net != null) net.PlaceCueResult += OnPlaceResult;
            }

            if (net == null || !net.HasSnapshot || local == null || _game.Paused || _game.MatchPaused)
            {
                Pulling = false;
                _pull = 0f;
                if (State != ShotState.None && (net == null || local == null)) Exit(local);
                return;
            }

            var view = net.View;
            var me = net.LocalPlayer;
            bool mine = view.InMatch && me != null && !me.Eliminated && view.CurrentPlayer == me.Id;
            bool shotPhase = mine && view.Phase == MatchPhase.AwaitShot;
            bool rolling = view.InMatch && (view.Phase == MatchPhase.ShotWindup || view.Phase == MatchPhase.Simulating || view.Phase == MatchPhase.ShotResult);

            if (State == ShotState.Watching)
            {
                UpdateWatching(net, local, view, rolling);
                return;
            }
            if (State == ShotState.Observing)
            {
                if (rolling) return;
                // Acabou de rolar: se ficou onde tacou e continua a ser a vez dele, a mira volta sozinha.
                bool stayed = (local.transform.position - _observeFrom).sqrMagnitude < ResumeAimRadius * ResumeAimRadius;
                State = ShotState.None;
                if (shotPhase && _resumeAim && stayed && !view.BallInHand)
                {
                    State = ShotState.Aiming;
                    InitAim(net, local, true);
                }
                _resumeAim = false;
            }

            if (!shotPhase)
            {
                if (State != ShotState.None) Exit(local);
                return;
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

            if (inputFree && keyboard != null && keyboard.qKey.wasPressedThisFrame && State != ShotState.PlacePending)
            {
                Exit(local);
                return;
            }

            if (State == ShotState.Placing) UpdatePlacing(net, local, keyboard, mouse, inputFree);
            else if (State == ShotState.PlacePending) UpdatePlacePending(net, local);
            else UpdateAiming(net, local, keyboard, mouse, inputFree);
        }

        void UpdateWatching(NetGame net, NetPlayer local, MatchView view, bool rolling)
        {
            _pull = Mathf.MoveTowards(_pull, 0f, Time.deltaTime * 3f);
            CameraPosition = _watchPosition;
            CameraRotation = _watchRotation;
            bool accepted = rolling || view.ShotSequence != _shotSentSequence;
            float since = Time.time - _shotTime;
            if (accepted && since >= FollowThroughSeconds)
            {
                BeginObserving(local);
                return;
            }
            // O anfitrião não aceitou a tacada (fora de tempo, sequência antiga): volta à mira.
            if (!accepted && since > 2f && view.Phase == MatchPhase.AwaitShot)
            {
                State = ShotState.Aiming;
                InitAim(net, local, true);
            }
        }

        void Enter(NetGame net, NetPlayer local)
        {
            Spin = Vector2.zero;
            if (net.View.BallInHand)
            {
                State = ShotState.Placing;
                local.MovementLocked = false;
                local.LookLocked = false;
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
                Vector3 cue = TableView.ToWorld(CuePosition(net), 0f);
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

        /// <summary>Solta a personagem no sítio da tacada, a olhar para a mesa: anda e olha à vontade.</summary>
        void BeginObserving(NetPlayer local)
        {
            State = ShotState.Observing;
            Pulling = false;
            TopView = false;
            _pull = 0f;
            _resumeAim = true;
            if (local == null) return;
            local.MovementLocked = false;
            local.LookLocked = false;
            local.Pose = AvatarPose.Idle;
            local.SettleOutsideTable(_worldYaw);
            local.SetLook(_worldYaw, Mathf.Clamp(_pitch + 12f, 18f, 45f));
            _observeFrom = local.transform.position;
        }

        void Exit(NetPlayer local)
        {
            bool hadStance = State == ShotState.Aiming || State == ShotState.Watching;
            State = ShotState.None;
            Pulling = false;
            TopView = false;
            _pull = 0f;
            _placeRequest = -1;
            if (local == null) return;
            local.MovementLocked = false;
            local.LookLocked = false;
            local.Pose = AvatarPose.Idle;
            if (!hadStance) return;
            local.SetLook(_worldYaw, 22f);
            local.SettleOutsideTable(_worldYaw);
        }

        // ------------------------------------------------------------------ bola na mão

        void UpdatePlacing(NetGame net, NetPlayer local, Keyboard keyboard, Mouse mouse, bool inputFree)
        {
            local.MovementLocked = false;
            local.LookLocked = false;
            local.Pose = AvatarPose.Idle;

            if (inputFree && keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame))
            {
                // Manter a branca onde está (se o sítio actual for válido — na abertura tem de estar atrás da linha).
                var sim0 = net.Mirror;
                if (sim0.Cue != null && sim0.IsValidCuePlacement(sim0.Cue.Position, net.View.KitchenOnly))
                {
                    State = ShotState.Aiming;
                    InitAim(net, local, false);
                    return;
                }
                PlacementHint = "A branca tem de mudar de sítio.";
            }

            var sim = net.Mirror;
            if (!PreviewFromCrosshair(net, out Vector2 raw)) return;
            PlacementVisible = true;
            bool kitchen = net.View.KitchenOnly;
            float r = sim.Settings.BallRadius;
            if (sim.TryFindPlacementNear(raw, kitchen, r * 3f, out var snapped))
            {
                PlacementPoint = snapped;
                PlacementValid = true;
            }
            else
            {
                PlacementPoint = raw;
                PlacementValid = false;
                PlacementHint = sim.DescribePlacement(raw, kitchen) ?? "Escolhe outro sítio.";
            }
            SendAim(net, new AimState { PlayerId = net.LocalPlayer.Id, Placing = true, PlacePosition = PlacementPoint });

            if (inputFree && mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (PlacementValid) SendPlacement(net, PlacementPoint);
                else _game.Sfx.PlayUi("uierror");
            }
        }

        /// <summary>Ponto da mesa sob a mira (plano do centro das bolas), limitado ao pano.</summary>
        bool PreviewFromCrosshair(NetGame net, out Vector2 point)
        {
            point = default;
            var cam = _game.Rig.Camera;
            var sim = net.Mirror;
            float r = sim.Settings.BallRadius;
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            var plane = new Plane(Vector3.up, new Vector3(0f, TableView.TopY + r, 0f));
            if (!plane.Raycast(ray, out float enter) || enter > 8f)
            {
                PlacementHint = "Aponta a mira para a mesa.";
                return false;
            }
            var table = sim.Table;
            Vector2 p = TableView.ToTable(ray.GetPoint(enter));
            if (Mathf.Abs(p.x) > table.HalfLength + 0.35f || Mathf.Abs(p.y) > table.HalfWidth + 0.35f)
            {
                PlacementHint = "Aponta a mira para a mesa.";
                return false;
            }
            point = p;
            return true;
        }

        void SendPlacement(NetGame net, Vector2 p)
        {
            // Estado e id registados ANTES de enviar: no anfitrião a resposta chega durante a própria chamada.
            _placeRequest = net.NextPlaceRequest;
            _placeSentAt = Time.time;
            _placeSentAt2 = p;
            PlacementPoint = p;
            State = ShotState.PlacePending;
            _game.Sfx.PlayUi("uiselect");
            net.RequestPlaceCue(p);
        }

        void UpdatePlacePending(NetGame net, NetPlayer local)
        {
            local.MovementLocked = false;
            local.LookLocked = false;
            PlacementVisible = true;
            PlacementValid = true;
            PlacementPoint = _placeSentAt2;
            if (Time.time - _placeSentAt > PlaceTimeout)
            {
                Refuse("O anfitrião não respondeu — tenta outra vez.");
                State = ShotState.Placing;
            }
        }

        void OnPlaceResult(int requestId, bool accepted, string reason, Vector2 position)
        {
            if (requestId != _placeRequest || State != ShotState.PlacePending) return;
            _placeRequest = -1;
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            if (net == null || local == null) return;
            if (!accepted)
            {
                Debug.Log("[Shot] bola na mão recusada: " + reason);
                Refuse(string.IsNullOrEmpty(reason) ? "Sítio recusado." : reason);
                State = ShotState.Placing;
                return;
            }
            Debug.Log("[Shot] bola na mão confirmada em " + position.ToString("0.00"));
            _cueOverride = position;
            _cueOverrideUntil = Time.time + 1f;
            _game.Sfx.PlayUi("uiconfirm");
            State = ShotState.Aiming;
            InitAim(net, local, false);
        }

        void Refuse(string reason)
        {
            LastRefusal = reason;
            LastRefusalTime = Time.unscaledTime;
            _game.Sfx.PlayUi("uierror");
        }

        // ------------------------------------------------------------------ mira e tacada

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
                _pull = 0f;
                return;
            }

            TopView = inputFree && keyboard != null && keyboard.tabKey.isPressed;
            Vector2 raw = inputFree && mouse != null ? mouse.delta.ReadValue() : Vector2.zero;
            float sensitivity = 0.075f * _game.Settings.MouseSensitivity;
            float fine = keyboard != null && keyboard.leftShiftKey.isPressed ? 0.18f : 1f;

            _worldYaw += raw.x * sensitivity * fine;
            if (!Pulling)
            {
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
                if (keyboard.wKey.isPressed) spin.y += rate * dt;
                if (keyboard.sKey.isPressed) spin.y -= rate * dt;
                if (keyboard.dKey.isPressed) spin.x += rate * dt;
                if (keyboard.aKey.isPressed) spin.x -= rate * dt;
                if (keyboard.cKey.wasPressedThisFrame) spin = Vector2.zero;
                SetSpin(spin);
            }

            if (inputFree && mouse != null && !Pulling && mouse.leftButton.wasPressedThisFrame)
            {
                Pulling = true;
                _pull = 0f;
            }
            var profile = FirstPersonRig.LocalShooterProfile(net, _game.Settings.ComfortMode);
            float sway = profile.AimSway > 0f ? (Mathf.PerlinNoise(Time.time * 0.6f, 3.3f) * 2f - 1f) * 7f * profile.AimSway : 0f;
            AimDirection = ShotMath.TableDirection(_worldYaw + sway);

            if (Pulling && !inputFree)
            {
                Pulling = false;
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

            if (State != ShotState.Aiming) return;
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
            Vector2 cue = CuePosition(net);
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
            _lastPower = Mathf.Max(0.05f, Pull01);
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
