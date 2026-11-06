using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Taco e assistência curta de mira. O atirador local vê a linha até ao primeiro contacto e a bola
    /// fantasma (vermelha se não for a bola legal); os outros vêem só o taco do atirador a mexer-se.
    /// </summary>
    public sealed class CueView : MonoBehaviour
    {
        GameBootstrap _game;
        Transform _cue;
        LineRenderer _aimLine;
        LineRenderer _ghostRing;
        LineRenderer _objectTick;
        Transform _placementGhost;
        Material _placementMaterial;
        float _remotePull;
        // Follow-through: depois da tacada o taco avança através da branca e só depois desaparece.
        float _follow = -1f;
        Vector3 _followPos, _followDir;
        int _followShooter = -1;
        int _lastShotSeq = -1;
        bool _cueShownLastFrame;
        Vector3 _lastCuePos, _lastCueDir;
        int _lastCueShooter = -1;
        float _lastCueLength = BaseLength;
        Vector3 _lastBridge;
        Transform _shaft, _butt, _extensionRing;
        const float BaseLength = 1.1f;

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            _aimLine = CreateLine("AimLine", 0.005f);
            _ghostRing = CreateLine("GhostRing", 0.004f);
            _ghostRing.loop = true;
            _objectTick = CreateLine("ObjectTick", 0.004f);

            var shaftMat = VisualFactory.Lit(new Color(0.86f, 0.72f, 0.48f), 0.6f);
            var buttMat = VisualFactory.Lit(new Color(0.15f, 0.08f, 0.05f), 0.7f);
            var tipMat = VisualFactory.Lit(new Color(0.2f, 0.45f, 0.85f), 0.2f);
            _cue = new GameObject("Cue").transform;
            _cue.SetParent(transform, false);
            _shaft = Rod(_cue, "Shaft", -0.4f, 0.4f, 0.012f, shaftMat);
            _butt = Rod(_cue, "Butt", -0.95f, 0.15f, 0.022f, buttMat);
            Rod(_cue, "Tip", -0.004f, 0.004f, 0.011f, tipMat);
            var brass = VisualFactory.Lit(ArtPalette.Mustard, 0.8f, 0.7f);
            _extensionRing = Rod(_cue, "Ring", -0.8f, 0.008f, 0.024f, brass);
            SetCueLength(BaseLength);

            _placementMaterial = VisualFactory.Lit(new Color(0.95f, 0.95f, 0.9f), 0.8f);
            _placementGhost = VisualFactory.Primitive(PrimitiveType.Sphere, "PlacementGhost", transform, Vector3.zero, Vector3.one * 0.057f, _placementMaterial).transform;
            HideAll();
        }

        static Transform Rod(Transform parent, string name, float z, float halfLength, float diameter, Material mat)
        {
            var t = VisualFactory.Primitive(PrimitiveType.Cylinder, name, parent, new Vector3(0, 0, z), new Vector3(diameter, halfLength, diameter), mat).transform;
            t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return t;
        }

        /// <summary>Haste clara da ponta até 30 cm do fim, anel de latão e punho escuro nos últimos 30 cm.</summary>
        void SetCueLength(float length)
        {
            float shaftLen = Mathf.Max(0.2f, length - 0.3f);
            _shaft.localPosition = new Vector3(0f, 0f, -shaftLen * 0.5f);
            var s = _shaft.localScale;
            _shaft.localScale = new Vector3(s.x, shaftLen * 0.5f, s.z);
            _butt.localPosition = new Vector3(0f, 0f, -shaftLen - 0.15f);
            _extensionRing.localPosition = new Vector3(0f, 0f, -shaftLen);
        }

        LineRenderer CreateLine(string lineName, float width)
        {
            var go = new GameObject(lineName);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.material = VisualFactory.Unlit(Color.white);
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        void HideAll()
        {
            _aimLine.enabled = false;
            _ghostRing.enabled = false;
            _objectTick.enabled = false;
            _cue.gameObject.SetActive(false);
            _placementGhost.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            HideAll();
            var net = NetGame.Instance;
            if (net == null || !net.HasSnapshot || !net.View.InMatch || net.Mirror.Cue == null) return;
            var view = net.View;

            // Nova tacada: se o taco estava visível no frame anterior, faz o movimento de acompanhamento.
            if (view.ShotSequence != _lastShotSeq)
            {
                if (_lastShotSeq >= 0 && _cueShownLastFrame && view.Phase == MatchPhase.Simulating)
                {
                    _follow = 0f;
                    _followPos = _lastCuePos;
                    _followDir = _lastCueDir;
                    _followShooter = _lastCueShooter;
                }
                _lastShotSeq = view.ShotSequence;
            }
            _cueShownLastFrame = false;
            if (_follow >= 0f)
            {
                _follow += Time.deltaTime;
                float k = Mathf.Clamp01(_follow / 0.12f);
                float push = (1f - (1f - k) * (1f - k)) * 0.16f;
                _cue.gameObject.SetActive(_follow < 0.55f);
                _cue.position = _followPos + _followDir * push;
                _cue.rotation = Quaternion.LookRotation(_followDir);
                var shooterAvatar = AvatarLocator.Find(net, _followShooter);
                if (shooterAvatar != null && _follow < 0.55f) shooterAvatar.SetCue(_cue.position, _followDir, _lastCueLength, _lastBridge);
                if (_follow >= 0.55f) _follow = -1f;
                if (view.Phase == MatchPhase.Simulating) return;
            }

            var shot = _game.Shot;
            var sim = net.Mirror;
            float r = sim.Settings.BallRadius;

            if (shot.PlacementVisible)
            {
                _placementGhost.gameObject.SetActive(true);
                _placementGhost.position = TableView.ToWorld(shot.PlacementPoint, r);
                VisualFactory.SetColor(_placementMaterial, shot.PlacementValid ? new Color(0.85f, 1f, 0.85f) : new Color(1f, 0.35f, 0.35f));
                return;
            }

            if (shot.State == ShotState.Aiming || (shot.State == ShotState.Watching && view.Phase == MatchPhase.AwaitShot))
            {
                DrawCue(sim.Cue.Position, shot.AimDirection, shot.PullDistance, shot.Spin, r, view.CurrentPlayer, true);
                if (shot.State == ShotState.Aiming) DrawAssist(sim, shot.AimDirection, r);
                return;
            }

            var aim = net.RemoteAim;
            var me = net.LocalPlayer;
            bool someoneElse = me == null || view.CurrentPlayer != me.Id;
            if (view.Phase == MatchPhase.AwaitShot && someoneElse && aim.PlayerId == view.CurrentPlayer)
            {
                if (aim.Placing)
                {
                    _placementGhost.gameObject.SetActive(true);
                    _placementGhost.position = TableView.ToWorld(aim.PlacePosition, r);
                    VisualFactory.SetColor(_placementMaterial, new Color(0.8f, 0.8f, 0.8f));
                    return;
                }
                _remotePull = Mathf.Lerp(_remotePull, aim.Pull * ShotController.MaxPull, Time.deltaTime * 12f);
                DrawCue(sim.Cue.Position, aim.Direction, _remotePull, aim.Spin, r, aim.PlayerId, false);
            }
        }

        void DrawCue(Vector2 cuePos, Vector2 direction, float pull, Vector2 spin, float r, int shooter, bool local)
        {
            var net = NetGame.Instance;
            var avatar = AvatarLocator.Find(net, shooter);
            // Os outros só vêem o taco quando quem joga já chegou ao sítio da tacada (não flutua enquanto caminha).
            if (!local && avatar != null)
            {
                Vector2 stand = ShotMath.StandBehind(cuePos, direction, net.Mirror.Table);
                Vector3 body = avatar.transform.position;
                if (new Vector2(body.x - stand.x, body.z - stand.y).sqrMagnitude > 0.45f * 0.45f) return;
            }

            Vector3 cueWorld = TableView.ToWorld(cuePos, r);
            var dir3 = new Vector3(direction.x, 0f, direction.y).normalized;
            var right = Vector3.Cross(Vector3.up, dir3);
            float tiltDeg = ShotMath.CueTilt(cuePos, direction, net.Mirror.Table, r);
            Vector3 tilt = Quaternion.AngleAxis(tiltDeg, right) * dir3;
            Vector3 tip0 = cueWorld - tilt * (r + 0.01f) + Vector3.up * (spin.y * r * 0.6f) + right * (spin.x * r * 0.6f);
            Vector3 tip = tip0 - tilt * pull;

            // Taco à medida do boneco: a mão direita agarra-o 20 cm atrás do ombro e sobra só a ponta do punho
            // (antes tinha 1,36 m e ficava a sair pelas costas). O punho está preso ao taco: ao puxar, a mão vai
            // com ele; a ponte fica pousada no pano, 18 cm atrás da branca.
            float length = 1.1f;
            Vector3 bridge = cueWorld - dir3 * 0.18f;
            bridge.y = TableView.TopY + 0.035f;
            if (avatar != null)
            {
                float shoulderT = Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt);
                length = Mathf.Clamp(shoulderT + 0.2f + AvatarVisual.CueGripFromButt, 0.75f, 2.4f);
                avatar.SetCue(tip, tilt, length, bridge);
            }
            _cue.gameObject.SetActive(true);
            _cue.position = tip;
            _cue.rotation = Quaternion.LookRotation(tilt);
            SetCueLength(length);
            _lastBridge = bridge;

            _cueShownLastFrame = true;
            _lastCuePos = _cue.position;
            _lastCueDir = tilt;
            _lastCueShooter = shooter;
            _lastCueLength = length;
        }

        void DrawAssist(FriendSlop.Billiards.BilliardsSimulation sim, Vector2 direction, float r)
        {
            bool hit = sim.CastFromCue(direction, out Vector2 ghost, out int hitBall);
            bool legal = !hit || hitBall == sim.LowestBallOnTable();
            Color color = legal ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 0.35f, 0.3f, 0.9f);

            Vector3 from = TableView.ToWorld(sim.Cue.Position, r);
            Vector3 ghostWorld = TableView.ToWorld(ghost, r);
            _aimLine.enabled = true;
            _aimLine.positionCount = 2;
            _aimLine.SetPosition(0, from);
            _aimLine.SetPosition(1, ghostWorld);
            _aimLine.startColor = _aimLine.endColor = color;

            if (!hit) return;
            const int segments = 24;
            _ghostRing.enabled = true;
            _ghostRing.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                _ghostRing.SetPosition(i, ghostWorld + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r);
            }
            _ghostRing.startColor = _ghostRing.endColor = color;

            var target = sim.GetBall(hitBall);
            Vector2 push = (target.Position - ghost).normalized;
            Vector3 targetWorld = TableView.ToWorld(target.Position, r);
            _objectTick.enabled = true;
            _objectTick.positionCount = 2;
            _objectTick.SetPosition(0, targetWorld);
            _objectTick.SetPosition(1, targetWorld + new Vector3(push.x, 0f, push.y) * 0.1f);
            _objectTick.startColor = _objectTick.endColor = new Color(1f, 0.9f, 0.4f, 0.8f);
        }
    }
}
