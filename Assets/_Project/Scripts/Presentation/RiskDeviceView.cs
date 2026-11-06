using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Apresentação do RiskResolution com o revólver. Sequência (tempos tirados da fase replicada, iguais em todos os
    /// clientes): o tambor gira no sorteio → a máquina atira o revólver à pessoa escolhida, que o apanha → leva-o à
    /// têmpora (IK, o cano encosta sem atravessar a cabeça) → tensão → gatilho → reacção: alívio e o revólver volta à
    /// máquina, ou impacto (frame de impacto + gosma vermelha) e eliminação. No modo de brincadeira depois da partida
    /// o revólver fica na mão do vencedor. O resultado vem sempre do servidor.
    /// </summary>
    public sealed class RiskDeviceView : MonoBehaviour
    {
        /// <summary>Fracções do tempo de "armar" (MatchConfig.TriggerArmSeconds): voo até à mão, depois subida à cabeça.</summary>
        const float FlyEnd = 0.4f, RaiseEnd = 0.92f;
        const float FireSeconds = 0.3f, ReliefStart = 0.4f, ReliefSeconds = 0.9f, ReturnSeconds = 0.8f;
        const float ModelScale = 1.7f;

        GameBootstrap _game;
        RevolverModel _gun;
        Light _flash;
        float _flashTime;
        float _drumAngle;
        float _drumSpeed;
        float _hammerKick;
        Vector3 _restPosition;
        Quaternion _restRotation;
        MatchPhase _lastPhase;
        Vector3 _returnFrom;
        Quaternion _returnFromRot;
        bool _returning;
        float _returnT;
        Vector3 _lastHeldPos;
        Quaternion _lastHeldRot;
        bool _hadHeld;
        float _scale = ModelScale;
        GameObject _viewHand;

        /// <summary>
        /// Mão em primeira pessoa: luva de punho fechado + manga, presas ao punho do revólver com a mesma pega das
        /// personagens (AvatarVisual.GripInHand) — quem segura a arma vê-se a segurá-la.
        /// </summary>
        void BuildViewHand()
        {
            var model = FsModels.Get("gloves", "GloveR_Fist");
            if (model == null) return;
            var ivory = VisualFactory.Lit(ArtPalette.Ivory, 0.45f);
            var suit = VisualFactory.Lit(ArtPalette.Hex(0x2A4566), 0.3f);
            var hand = new GameObject("ViewHand").transform;
            hand.SetParent(_gun.Root, false);
            var localRot = Quaternion.Inverse(AvatarVisual.GripInHand);
            hand.localRotation = localRot;
            hand.localPosition = AvatarVisual.GunGloveLocal - localRot * (AvatarVisual.FistChannelLocal * AvatarVisual.FistScale);
            var fist = LowPoly.Pivot("Fist", hand, Vector3.zero);
            fist.localScale = Vector3.one * AvatarVisual.FistScale;
            FsModels.Spawn(model, fist, Vector3.zero, Quaternion.identity, Vector3.one, n => ivory);
            // Manga (antebraço) a subir do pulso para trás, para fora do ecrã.
            LowPoly.Part(ToyMeshes.Capsule(0.05f, 0.34f), "Sleeve", hand, new Vector3(0f, 0.36f, 0f), Vector3.one, suit);
            foreach (var r in hand.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _viewHand = hand.gameObject;
            _viewHand.SetActive(false);
        }

        /// <summary>Instante (tempo não escalado) do último disparo com bala, para o frame de impacto da HUD.</summary>
        public float ImpactAt { get; private set; } = -10f;
        public bool LocalImpact { get; private set; }

        public void Initialize(GameBootstrap game, Vector3 restPosition, Quaternion restRotation)
        {
            _game = game;
            _gun = RevolverModel.Build(transform);
            _gun.Root.localScale = Vector3.one * ModelScale;
            _restPosition = restPosition;
            _restRotation = restRotation;
            _gun.Root.SetPositionAndRotation(_restPosition, _restRotation);

            var flashGo = new GameObject("MuzzleFlash");
            flashGo.transform.SetParent(_gun.Muzzle, false);
            _flash = flashGo.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.color = new Color(1f, 0.55f, 0.35f);
            _flash.range = 5f;
            _flash.intensity = 0f;
            _flash.shadows = LightShadows.None;
            BuildViewHand();
        }

        public void Bind(NetGame net)
        {
            net.RiskResolved += OnRiskResolved;
            net.FunShot += OnFunShot;
        }

        public Vector3 MuzzlePosition => _gun.Muzzle.position;

        void OnRiskResolved(int playerId, RiskOutcome outcome, int modifierDef, bool eliminated)
        {
            _hammerKick = 1f;
            var net = NetGame.Instance;
            bool local = net != null && net.LocalPlayer != null && net.LocalPlayer.Id == playerId;
            if (outcome != RiskOutcome.Discharge)
            {
                _game.Rig.Shake(local ? 0.01f : 0f);
                return;
            }

            _flashTime = 0.25f;
            ImpactAt = Time.unscaledTime;
            LocalImpact = local;
            var avatar = AvatarLocator.Find(net, playerId);
            Vector3 origin = avatar != null && avatar.Head != null ? avatar.Head.position + Vector3.up * 0.2f : _gun.Muzzle.position;
            _game.Goo.Burst(origin, eliminated ? 1.3f : 0.6f, avatar != null ? avatar.transform.parent : null);
            if (avatar != null)
            {
                if (eliminated) avatar.Pop();
                else avatar.Knockdown();
            }
            _game.Sfx.PlayGoo(origin, eliminated);
            _game.Rig.Shake(local ? 0.08f : 0.03f);
            if (local) _game.Rig.KnockOver();
        }

        void OnFunShot(int shooter, int target, Vector3 origin, Vector3 hit)
        {
            var net = NetGame.Instance;
            _flashTime = 0.12f;
            _hammerKick = 1f;
            AvatarLocator.Find(net, shooter)?.GunKick();
        }

        void Update()
        {
            // Pedidos de pose (braço/revólver) para as personagens; o LateUpdate coloca o revólver na mão resultante.
            var net = NetGame.Instance;
            var view = net != null && net.HasSnapshot ? net.View : null;
            if (view == null || !view.InMatch) return;
            if (view.Phase == MatchPhase.MatchOver && view.MatchWinner >= 0)
            {
                var winner = AvatarLocator.Find(net, view.MatchWinner);
                if (winner != null) winner.SetGun(GunStage.Aim, 0f, FunAim(net, view.MatchWinner, winner));
                return;
            }
            var avatar = AvatarLocator.Find(net, view.Selected);
            if (avatar == null) return;
            float arm = TriggerArm();
            float elapsed = view.PhaseDuration - view.PhaseRemaining;
            if (view.Phase == MatchPhase.AwaitTrigger)
            {
                float k = elapsed / arm;
                if (k < FlyEnd) avatar.SetGun(GunStage.Reach, 0f, default, _gun.Root.position);
                else if (k < RaiseEnd) avatar.SetGun(GunStage.Raise, (k - FlyEnd) / (RaiseEnd - FlyEnd));
                else avatar.SetGun(GunStage.Hold, Mathf.Clamp01((elapsed - arm) / 6f));
            }
            else if (view.Phase == MatchPhase.RiskReveal && view.HasRiskResult)
            {
                if (elapsed < FireSeconds) avatar.SetGun(GunStage.Fire, elapsed / FireSeconds);
                else if (view.RiskOutcome != RiskOutcome.Discharge && elapsed < view.PhaseDuration - ReturnSeconds)
                    avatar.SetGun(elapsed < ReliefStart ? GunStage.Hold : GunStage.Relief, elapsed < ReliefStart ? 1f : (elapsed - ReliefStart) / ReliefSeconds);
            }
        }

        static float TriggerArm() => NetGame.SharedConfig != null ? Mathf.Max(0.6f, NetGame.SharedConfig.TriggerArmSeconds) : 1.6f;

        /// <summary>Para onde o vencedor aponta no modo de brincadeira: o olhar (humano) ou a frente do boneco (bot).</summary>
        static Vector3 FunAim(NetGame net, int playerId, AvatarVisual avatar)
        {
            var p = net.View.GetPlayer(playerId);
            if (p != null && !p.IsBot)
            {
                var np = NetPlayer.ForClient(p.ClientId);
                // BodyYaw: nos outros clientes o Yaw do vencedor ficava parado no valor do teletransporte e o braço
                // apontava para um rumo fixo do mundo (de lado ou para trás do boneco).
                if (np != null) return Quaternion.Euler(Mathf.Clamp(np.Pitch, -40f, 40f), FunModeController.Legacy ? np.Yaw : np.BodyYaw, 0f) * Vector3.forward;
            }
            return avatar.transform.forward;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var net = NetGame.Instance;
            var view = net != null && net.HasSnapshot ? net.View : null;

            Vector3 targetPos = _restPosition;
            Quaternion targetRot = _restRotation;
            float follow = 6f;
            float hammerCock = 0f;
            float scale = ModelScale;
            _drumSpeed = Mathf.MoveTowards(_drumSpeed, 0f, dt * 900f);
            bool snap = false;
            bool viewModel = false;

            if (view != null && view.InMatch)
            {
                if (view.Phase == MatchPhase.RoundIntro && _lastPhase != MatchPhase.RoundIntro && view.RoundIndex <= 1) _game.Goo.ClearAll();
                if (view.Phase != _lastPhase)
                {
                    // O revólver volta à máquina a partir de onde estava (mão ou câmara), em arco.
                    bool wasHeld = _lastPhase == MatchPhase.AwaitTrigger || _lastPhase == MatchPhase.RiskReveal || _lastPhase == MatchPhase.MatchOver;
                    if (wasHeld && view.Phase != MatchPhase.RiskReveal && view.Phase != MatchPhase.MatchOver) BeginReturn();
                }
                _lastPhase = view.Phase;

                float elapsed = view.PhaseDuration - view.PhaseRemaining;
                bool local = net.LocalPlayer != null && net.LocalPlayer.Id == view.Selected;
                if (view.Phase == MatchPhase.PunishmentDraw)
                {
                    _drumSpeed = Mathf.Max(_drumSpeed, 700f);
                }
                else if (view.Phase == MatchPhase.MatchOver && view.MatchWinner >= 0)
                {
                    bool mine = net.LocalPlayer != null && net.LocalPlayer.Id == view.MatchWinner;
                    if (mine && _game.Rig.Camera != null && !_game.Hud.ResultsOpen)
                    {
                        var cam = _game.Rig.Camera.transform;
                        float kick = _hammerKick;
                        targetPos = cam.TransformPoint(new Vector3(0.19f, -0.17f - kick * 0.02f, 0.42f - kick * 0.05f));
                        targetRot = cam.rotation * Quaternion.Euler(-3f - kick * 18f, -4f, 0f);
                        follow = 30f;
                        scale = 1f;
                        viewModel = true;
                    }
                    else if (TryHeld(net, view.MatchWinner, out var p, out var r))
                    {
                        targetPos = p;
                        targetRot = r;
                        snap = true;
                        scale = AvatarVisual.HeldGunScale;
                    }
                    hammerCock = 1f - _hammerKick;
                }
                else if ((view.Phase == MatchPhase.AwaitTrigger || view.Phase == MatchPhase.RiskReveal) && view.Selected >= 0)
                {
                    float arm = TriggerArm();
                    float k = view.Phase == MatchPhase.AwaitTrigger ? elapsed / arm : 1f;
                    hammerCock = view.Phase == MatchPhase.AwaitTrigger ? Mathf.Clamp01((k - FlyEnd) / (RaiseEnd - FlyEnd)) : Mathf.Clamp01(1f - elapsed * 8f);
                    if (view.Phase == MatchPhase.AwaitTrigger) _drumSpeed = Mathf.Max(_drumSpeed, k < RaiseEnd ? 260f : 20f);
                    bool returning = view.Phase == MatchPhase.RiskReveal &&
                                     (view.RiskOutcome == RiskOutcome.Discharge ? elapsed > 0.6f : elapsed > view.PhaseDuration - ReturnSeconds);

                    if (view.Phase == MatchPhase.AwaitTrigger && k < FlyEnd)
                    {
                        // A máquina atira o revólver: arco da bandeja até à mão que se estica, a rodar.
                        _returning = false;
                        Vector3 hand = HandTarget(net, view.Selected, local);
                        float u = Mathf.Clamp01(k / FlyEnd);
                        float e = u * u * (3f - 2f * u);
                        Vector3 mid = Vector3.Lerp(_restPosition, hand, 0.5f) + Vector3.up * (0.6f + Vector3.Distance(_restPosition, hand) * 0.12f);
                        targetPos = Vector3.Lerp(Vector3.Lerp(_restPosition, mid, e), Vector3.Lerp(mid, hand, e), e);
                        targetRot = Quaternion.Slerp(_restRotation, Quaternion.LookRotation(hand - _restPosition + Vector3.up * 0.3f), e) * Quaternion.Euler(u * 540f, 0f, 0f);
                        scale = Mathf.Lerp(ModelScale, local ? 1f : AvatarVisual.HeldGunScale, e);
                        snap = true;
                    }
                    else if (returning)
                    {
                        if (!_returning) BeginReturn();
                    }
                    else if (local && _game.Rig.Camera != null)
                    {
                        // Primeira pessoa: o revólver sobe da parte de baixo do ecrã e fica encostado à têmpora direita.
                        var cam = _game.Rig.Camera.transform;
                        float raise = view.Phase == MatchPhase.AwaitTrigger ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - FlyEnd) / (RaiseEnd - FlyEnd))) : 1f;
                        float relief = view.Phase == MatchPhase.RiskReveal && view.RiskOutcome != RiskOutcome.Discharge ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - ReliefStart) / ReliefSeconds)) : 0f;
                        float shake = hammerCock * 0.003f * (view.Phase == MatchPhase.AwaitTrigger ? 1f : 0f);
                        Vector3 low = new Vector3(0.24f, -0.42f, 0.36f);
                        Vector3 temple = new Vector3(0.21f, -0.02f, 0.2f);
                        Vector3 p = Vector3.Lerp(Vector3.Lerp(low, temple, raise), low, relief);
                        p += new Vector3(Random.Range(-shake, shake), Random.Range(-shake, shake), 0f);
                        targetPos = cam.TransformPoint(p);
                        // O cano aponta para a têmpora (ligeiramente à direita do centro da cabeça).
                        Vector3 aimAt = Vector3.Lerp(new Vector3(0.1f, 0.2f, 1.2f), new Vector3(0.05f, 0.02f, -0.02f), raise * (1f - relief));
                        targetRot = Quaternion.LookRotation(cam.TransformDirection((aimAt - p).normalized), cam.up);
                        if (view.Phase == MatchPhase.RiskReveal && elapsed < FireSeconds)
                            targetRot *= Quaternion.Euler(-22f * Mathf.Sin(elapsed / FireSeconds * Mathf.PI), 0f, 0f);
                        follow = 28f;
                        scale = 1f;
                        viewModel = true;
                    }
                    else if (TryHeld(net, view.Selected, out var p, out var r))
                    {
                        targetPos = p;
                        targetRot = r;
                        snap = true;
                        scale = AvatarVisual.HeldGunScale;
                    }
                }
            }
            else
            {
                _lastPhase = MatchPhase.None;
            }

            var root = _gun.Root;
            if (_returning)
            {
                // Volta à bandeja da máquina num arco.
                _returnT += dt / ReturnSeconds;
                float u = Mathf.Clamp01(_returnT);
                float e = u * u * (3f - 2f * u);
                Vector3 mid = Vector3.Lerp(_returnFrom, _restPosition, 0.5f) + Vector3.up * 0.7f;
                targetPos = Vector3.Lerp(Vector3.Lerp(_returnFrom, mid, e), Vector3.Lerp(mid, _restPosition, e), e);
                targetRot = Quaternion.Slerp(_returnFromRot, _restRotation, e) * Quaternion.Euler((1f - e) * 360f, 0f, 0f);
                scale = Mathf.Lerp(_scale, ModelScale, e);
                snap = true;
                if (u >= 1f) _returning = false;
            }

            if (snap)
            {
                root.SetPositionAndRotation(targetPos, targetRot);
            }
            else
            {
                root.position = Vector3.Lerp(root.position, targetPos, 1f - Mathf.Exp(-dt * follow));
                root.rotation = Quaternion.Slerp(root.rotation, targetRot, 1f - Mathf.Exp(-dt * follow));
            }
            _scale = Mathf.Lerp(_scale, scale, snap ? 1f : 1f - Mathf.Exp(-dt * 14f));
            root.localScale = Vector3.one * _scale;
            _lastHeldPos = root.position;
            _lastHeldRot = root.rotation;
            if (_viewHand != null && _viewHand.activeSelf != (viewModel && !_returning)) _viewHand.SetActive(viewModel && !_returning);

            int loaded = view != null && view.InMatch ? view.LoadedChambers : 1;
            for (int i = 0; i < _gun.Bullets.Length; i++)
                if (_gun.Bullets[i] != null && _gun.Bullets[i].activeSelf != (i < loaded)) _gun.Bullets[i].SetActive(i < loaded);

            _drumAngle += _drumSpeed * dt;
            _gun.Cylinder.localRotation = Quaternion.Euler(0f, 0f, _drumAngle);

            _hammerKick = Mathf.MoveTowards(_hammerKick, 0f, dt * 4f);
            float hammer = hammerCock * -45f + _hammerKick * 20f;
            _gun.Hammer.localRotation = Quaternion.Euler(hammer, 0f, 0f);
            float trigger = view != null && view.Phase == MatchPhase.AwaitTrigger && net.LocalPlayer != null && net.LocalPlayer.Id == view.Selected ? _game.Hud.TriggerHold : _hammerKick;
            _gun.Trigger.localRotation = Quaternion.Euler(trigger * 25f, 0f, 0f);

            _flashTime = Mathf.Max(0f, _flashTime - dt);
            _flash.intensity = _flashTime > 0f ? 30f * (_flashTime / 0.25f) : 0f;
        }

        void BeginReturn()
        {
            _returning = true;
            _returnT = 0f;
            _returnFrom = _gun.Root.position;
            _returnFromRot = _gun.Root.rotation;
        }

        static bool TryHeld(NetGame net, int playerId, out Vector3 position, out Quaternion rotation)
        {
            var avatar = AvatarLocator.Find(net, playerId);
            position = default;
            rotation = Quaternion.identity;
            return avatar != null && avatar.TryGetHeldGun(out position, out rotation);
        }

        Vector3 HandTarget(NetGame net, int playerId, bool local)
        {
            if (local && _game.Rig.Camera != null) return _game.Rig.Camera.transform.TransformPoint(new Vector3(0.24f, -0.42f, 0.36f));
            var avatar = AvatarLocator.Find(net, playerId);
            if (avatar != null && avatar.RightHand != null) return avatar.RightHand.position;
            return _restPosition + Vector3.up;
        }
    }
}
