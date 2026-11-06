using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Apresentação do RiskResolution: revólver de brinquedo da roleta. Descansa no tabuleiro da máquina de
    /// sorteio; o tambor gira durante o sorteio; vai para a mão de quem foi escolhido (view-model em primeira
    /// pessoa para o próprio). No disparo, gosma alienígena em vez de gore. O resultado vem sempre do servidor.
    /// </summary>
    public sealed class RiskDeviceView : MonoBehaviour
    {
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

        public void Initialize(GameBootstrap game, Vector3 restPosition, Quaternion restRotation)
        {
            _game = game;
            _gun = RevolverModel.Build(transform);
            _gun.Root.localScale = Vector3.one * 1.7f;
            _restPosition = restPosition;
            _restRotation = restRotation;
            _gun.Root.SetPositionAndRotation(_restPosition, _restRotation);

            var flashGo = new GameObject("MuzzleFlash");
            flashGo.transform.SetParent(_gun.Muzzle, false);
            _flash = flashGo.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.color = new Color(0.6f, 1f, 0.35f);
            _flash.range = 5f;
            _flash.intensity = 0f;
        }

        public void Bind(NetGame net) => net.RiskResolved += OnRiskResolved;

        void OnRiskResolved(int playerId, RiskOutcome outcome, int modifierDef, bool eliminated)
        {
            _hammerKick = 1f;
            _drumSpeed = 1400f;
            var net = NetGame.Instance;
            bool local = net != null && net.LocalPlayer != null && net.LocalPlayer.Id == playerId;
            if (outcome != RiskOutcome.Discharge)
            {
                _game.Rig.Shake(local ? 0.012f : 0f);
                return;
            }

            _flashTime = 0.3f;
            var avatar = AvatarLocator.Find(net, playerId);
            Vector3 origin = avatar != null && avatar.Head != null ? avatar.Head.position + Vector3.up * 0.2f : _gun.Muzzle.position;
            _game.Goo.Burst(origin, eliminated ? 1.3f : 0.6f);
            if (avatar != null)
            {
                if (eliminated) avatar.Pop();
                else avatar.Knockdown();
            }
            _game.Sfx.PlayGoo(origin, eliminated);
            _game.Rig.Shake(local ? 0.08f : 0.03f);
            if (local) _game.Rig.KnockOver();
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
            _drumSpeed = Mathf.MoveTowards(_drumSpeed, 0f, dt * 900f);

            if (view != null && view.InMatch)
            {
                if (view.Phase == MatchPhase.RoundIntro && _lastPhase != MatchPhase.RoundIntro && view.RoundIndex <= 1) _game.Goo.ClearAll();
                _lastPhase = view.Phase;

                if (view.Phase == MatchPhase.PunishmentDraw)
                {
                    _drumSpeed = Mathf.Max(_drumSpeed, 700f);
                }
                else if ((view.Phase == MatchPhase.AwaitTrigger || view.Phase == MatchPhase.RiskReveal) && view.Selected >= 0)
                {
                    bool local = net.LocalPlayer != null && net.LocalPlayer.Id == view.Selected;
                    if (view.Phase == MatchPhase.AwaitTrigger)
                    {
                        _drumSpeed = Mathf.Max(_drumSpeed, 40f);
                        hammerCock = local ? _game.Hud.TriggerHold : Mathf.PingPong(Time.time * 0.8f, 0.6f);
                    }

                    if (local && _game.Rig.Camera != null)
                    {
                        var cam = _game.Rig.Camera.transform;
                        float shake = hammerCock * 0.004f;
                        targetPos = cam.TransformPoint(new Vector3(0.2f + Random.Range(-shake, shake), -0.2f + Random.Range(-shake, shake), 0.45f));
                        targetRot = cam.rotation * Quaternion.Euler(-12f, -14f, 8f);
                        follow = 22f;
                    }
                    else
                    {
                        var avatar = AvatarLocator.Find(net, view.Selected);
                        if (avatar != null && avatar.RightHand != null)
                        {
                            targetPos = avatar.RightHand.position + avatar.RightHand.up * -0.02f;
                            targetRot = avatar.transform.rotation * Quaternion.Euler(-70f, -35f, 0f);
                            follow = 16f;
                        }
                    }
                }
            }
            else
            {
                _lastPhase = MatchPhase.None;
            }

            var root = _gun.Root;
            root.position = Vector3.Lerp(root.position, targetPos, 1f - Mathf.Exp(-dt * follow));
            root.rotation = Quaternion.Slerp(root.rotation, targetRot, 1f - Mathf.Exp(-dt * follow));

            int loaded = view != null && view.InMatch ? view.LoadedChambers : 1;
            for (int i = 0; i < _gun.Bullets.Length; i++)
                if (_gun.Bullets[i] != null && _gun.Bullets[i].activeSelf != (i < loaded)) _gun.Bullets[i].SetActive(i < loaded);

            _drumAngle += _drumSpeed * dt;
            _gun.Cylinder.localRotation = Quaternion.Euler(0f, 0f, _drumAngle);

            _hammerKick = Mathf.MoveTowards(_hammerKick, 0f, dt * 4f);
            float hammer = hammerCock * -60f + _hammerKick * 25f;
            _gun.Hammer.localRotation = Quaternion.Euler(hammer, 0f, 0f);
            _gun.Trigger.localRotation = Quaternion.Euler(hammerCock * 25f, 0f, 0f);

            _flashTime = Mathf.Max(0f, _flashTime - dt);
            _flash.intensity = _flashTime > 0f ? 30f * (_flashTime / 0.3f) : 0f;
        }
    }
}
