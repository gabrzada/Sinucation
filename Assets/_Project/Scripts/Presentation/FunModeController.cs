using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Modo de brincadeira depois do placar final: o vencedor dispara (clique esquerdo) contra quem perdeu; quem é
    /// atingido cai em ragdoll ~1 s e levanta-se. O anfitrião valida cada tiro (NetGame.FunShot) e todos os clientes
    /// reproduzem o mesmo evento. Não altera resultado, pontos nem recompensas.
    /// </summary>
    public sealed class FunModeController : MonoBehaviour
    {
        /// <summary>
        /// QA (-fsFunLegacy): repõe o comportamento antigo (mira do revólver nos outros clientes, levantar do ragdoll e
        /// câmara/corpo de quem leva o tiro), para comparar antes/depois na mesma build.
        /// </summary>
        public static bool Legacy;

        GameBootstrap _game;
        NetGame _bound;
        float _nextShot;
        LineRenderer _tracer;
        float _tracerUntil;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            var go = new GameObject("FunTracer");
            go.transform.SetParent(transform, false);
            _tracer = go.AddComponent<LineRenderer>();
            _tracer.useWorldSpace = true;
            _tracer.widthMultiplier = 0.012f;
            _tracer.material = VisualFactory.Unlit(Color.white);
            _tracer.numCapVertices = 2;
            _tracer.positionCount = 2;
            _tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _tracer.enabled = false;
        }

        void Bind(NetGame net)
        {
            _bound = net;
            net.FunShot += OnFunShot;
            // No anfitrião, a apresentação diz onde está cada boneco (validação dos tiros e mira dos bots).
            net.FunTargetPosition = id =>
            {
                var avatar = AvatarLocator.Find(net, id);
                return avatar != null ? avatar.transform.position : (Vector3?)null;
            };
        }

        void Update()
        {
            var net = NetGame.Instance;
            if (net != null && net != _bound && net.IsSpawned) Bind(net);
            if (_tracer.enabled && Time.time > _tracerUntil) _tracer.enabled = false;
            if (_tracer.enabled)
            {
                float k = Mathf.Clamp01((_tracerUntil - Time.time) / 0.09f);
                _tracer.startColor = new Color(1f, 0.95f, 0.7f, k);
                _tracer.endColor = new Color(1f, 0.6f, 0.3f, k * 0.4f);
            }
            if (net == null || !net.LocalIsFunShooter || _game.CursorFree || _game.Paused || _game.MatchPaused || _game.Hud.ResultsOpen) return;
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame || Time.time < _nextShot) return;
            _nextShot = Time.time + NetGame.FunShotCooldown;

            var cam = _game.Rig.Camera.transform;
            var ray = new Ray(cam.position, cam.forward);
            int target = -1;
            Vector3 point = ray.GetPoint(20f);
            int count = Physics.RaycastNonAlloc(ray, _hits, 30f, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            var local = NetPlayer.Local;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null || (local != null && hit.collider.transform.IsChildOf(local.transform))) continue;
                if (hit.distance >= best) continue;
                best = hit.distance;
                point = hit.point;
                target = PlayerFor(net, hit.collider);
            }
            if (target == net.View.MatchWinner) target = -1;
            net.RequestFunShot(cam.position, cam.forward, target, point);
        }

        /// <summary>Que jogador tem este collider (cápsula do jogador remoto ou do bot).</summary>
        static int PlayerFor(NetGame net, Collider collider)
        {
            foreach (var p in net.View.Players)
            {
                var avatar = AvatarLocator.Find(net, p.Id);
                if (avatar == null || avatar.transform.parent == null) continue;
                if (collider.transform == avatar.transform.parent || collider.transform.IsChildOf(avatar.transform.parent)) return p.Id;
            }
            return -1;
        }

        void OnFunShot(int shooter, int target, Vector3 origin, Vector3 hit)
        {
            var net = NetGame.Instance;
            if (net == null) return;
            Vector3 muzzle = _game.Device != null ? _game.Device.MuzzlePosition : origin;
            _tracer.enabled = true;
            _tracer.SetPosition(0, muzzle);
            _tracer.SetPosition(1, hit);
            _tracerUntil = Time.time + 0.09f;
            _game.Sfx.Play3D("bang", muzzle, 0.55f, 1.35f);
            if (target < 0)
            {
                _game.Sfx.Play3D("thud", hit, 0.35f, 1.2f);
                return;
            }
            var avatar = AvatarLocator.Find(net, target);
            Vector3 dir = hit - origin;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
            // Impulso fixo (não soma com tiros anteriores; o anfitrião também não deixa acertar em quem está no chão).
            Vector3 impulse = dir * 3.4f + Vector3.up * 1.1f;
            if (avatar != null) avatar.Ragdoll(impulse, NetGame.FunRagdollSeconds);
            _game.Sfx.Play3D("punch", hit, 0.8f, Random.Range(0.9f, 1.1f));
            _game.Sfx.PlayCrowd("crowdlaugh", 0.16f);
            var me = net.LocalPlayer;
            if (me != null && me.Id == target && NetPlayer.Local != null)
            {
                NetPlayer.Local.Stun(NetGame.FunRagdollSeconds + 0.5f);
                _game.Rig.Shake(0.05f);
                // Também nos modos de câmara confortáveis (em que a vista quase não mexe) fica claro o que aconteceu.
                if (!Legacy) _game.Hud.ShowToast("Levaste um tiro de " + (net.View.GetPlayer(shooter)?.Profile.Name ?? "alguém") + "! No chão por um segundo.", 2.2f);
            }
        }
    }
}
