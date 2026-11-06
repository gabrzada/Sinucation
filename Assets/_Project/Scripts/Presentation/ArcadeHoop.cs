using FriendSlop.Core;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Cesta de arcade móvel na parede sul (à direita da TV, longe da mesa): a tabela desliza para um lado e para o outro
    /// em função do relógio de rede (HoopRules.Offset), por isso todos a vêem no mesmo sítio sem replicar posições. Os
    /// objectos atirados ([G]) batem no aro e na tabela em PhysX local; só o anfitrião decide a cesta (RimTrack) e pede o
    /// prémio ao NetGame. O "+1" e o som aparecem a todos quando o anfitrião confirma.
    /// </summary>
    public sealed class ArcadeHoop : MonoBehaviour
    {
        /// <summary>Altura do plano do aro.</summary>
        public const float RimY = 2.5f;
        /// <summary>Distância do centro do aro à parede.</summary>
        public const float RimOut = 0.53f;
        const float BoardY = 2.74f;
        const float BoardZ = 0.25f;
        const float PopupSeconds = 1.6f;

        GameBootstrap _game;
        NetGame _bound;
        Transform _carriage;
        Rigidbody _body;
        Light _flash;
        TextMesh _popup;
        float _popupAt = -10f;
        double _clockOffset;
        bool _hasClock;

        /// <summary>Centro do aro no mundo, onde a física o tem agora.</summary>
        public Vector3 RimCenter => (_body != null ? _body.position : _carriage.position) + transform.rotation * new Vector3(0f, RimY, RimOut);
        /// <summary>Cestas confirmadas pelo anfitrião vistas por este peer (QA).</summary>
        public int BasketsSeen { get; private set; }
        public event System.Action<ulong, int, int> BasketShown;

        public void Build(GameBootstrap game)
        {
            _game = game;
            var navy = VisualFactory.Lit(ArtPalette.NavyDark, 0.35f, 0.3f);
            var frame = VisualFactory.Lit(ArtPalette.Mustard, 0.5f, 0.35f);
            var steel = VisualFactory.Lit(new Color(0.62f, 0.64f, 0.68f), 0.65f, 0.8f);
            var board = VisualFactory.Lit(ArtPalette.Ivory, 0.55f);
            var orange = VisualFactory.Lit(new Color(1f, 0.42f, 0.08f), 0.55f, 0.6f);
            var net = VisualFactory.Lit(new Color(0.95f, 0.94f, 0.9f), 0.2f);
            var neon = VisualFactory.Emissive(ArtPalette.Mustard, 5f);
            var coral = VisualFactory.Emissive(ArtPalette.Coral, 4f);
            var box = LowPoly.BevelBox(0.03f);

            // Painel do arcade preso à parede: fundo azul, moldura mostarda, calha onde corre a tabela e letreiro em néon.
            float span = HoopRules.SwayAmplitude * 2f + 1.05f;
            Solid(box, "HoopPanel", transform, new Vector3(0f, 2.95f, 0.04f), new Vector3(span, 1.55f, 0.08f), navy);
            foreach (float y in new[] { 2.17f, 3.73f })
                Shadowless(LowPoly.Part(box, "HoopFrame", transform, new Vector3(0f, y, 0.09f), new Vector3(span + 0.08f, 0.07f, 0.05f), frame));
            foreach (float x in new[] { -span * 0.5f, span * 0.5f })
                Shadowless(LowPoly.Part(box, "HoopFrame", transform, new Vector3(x, 2.95f, 0.09f), new Vector3(0.07f, 1.62f, 0.05f), frame));
            Shadowless(LowPoly.Part(box, "HoopRail", transform, new Vector3(0f, BoardY, 0.12f), new Vector3(span - 0.1f, 0.06f, 0.06f), steel));
            Shadowless(LowPoly.Part(box, "HoopSignBack", transform, new Vector3(0f, 4.02f, 0.05f), new Vector3(1.5f, 0.42f, 0.06f), navy));
            VisualFactory.Text(transform, "CESTA +1", new Vector3(0f, 4.02f, 0.09f), 0.3f, ArtPalette.Mustard).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            foreach (float x in new[] { -0.82f, 0.82f })
                Shadowless(LowPoly.Part(ToyMeshes.Sphere, "HoopBulb", transform, new Vector3(x, 4.02f, 0.1f), Vector3.one * 0.08f, coral));

            // Carro móvel: tabela, quadrado de mira, suporte, aro e rede. Um corpo cinemático empurra os objectos atirados.
            _carriage = LowPoly.Pivot("HoopCarriage", transform, Vector3.zero);
            Solid(box, "Backboard", _carriage, new Vector3(0f, BoardY, BoardZ), new Vector3(0.9f, 0.6f, 0.04f), board);
            Shadowless(LowPoly.Part(box, "BoardTrim", _carriage, new Vector3(0f, BoardY, BoardZ - 0.01f), new Vector3(0.96f, 0.66f, 0.03f), frame));
            foreach (var (pos, size) in new[]
            {
                (new Vector3(0f, RimY + 0.04f, BoardZ + 0.025f), new Vector3(0.36f, 0.03f, 0.01f)),
                (new Vector3(0f, RimY + 0.3f, BoardZ + 0.025f), new Vector3(0.36f, 0.03f, 0.01f)),
                (new Vector3(-0.165f, RimY + 0.17f, BoardZ + 0.025f), new Vector3(0.03f, 0.27f, 0.01f)),
                (new Vector3(0.165f, RimY + 0.17f, BoardZ + 0.025f), new Vector3(0.03f, 0.27f, 0.01f)),
            })
                Shadowless(LowPoly.Part(box, "BoardSquare", _carriage, pos, size, neon));
            Shadowless(LowPoly.Part(box, "RimBracket", _carriage, new Vector3(0f, RimY - 0.03f, BoardZ + 0.035f), new Vector3(0.14f, 0.05f, 0.06f), orange));

            var rimCenter = new Vector3(0f, RimY, RimOut);
            var rim = LowPoly.Part(LowPoly.Ring(24, HoopRules.RimRadius + 0.012f, 0.024f, 0.024f), "Rim", _carriage, rimCenter, Vector3.one, orange);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Colisão do aro: 16 segmentos tangentes (os objectos podem bater e ressaltar no ferro).
            const int segments = 16;
            float segment = 2f * Mathf.PI * (HoopRules.RimRadius + 0.012f) / segments * 1.08f;
            for (int i = 0; i < segments; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var piece = new GameObject("RimCollider");
                piece.transform.SetParent(_carriage, false);
                piece.transform.localPosition = rimCenter + dir * (HoopRules.RimRadius + 0.012f);
                piece.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(Vector3.up, dir), Vector3.up);
                piece.AddComponent<BoxCollider>().size = new Vector3(0.026f, 0.026f, segment);
            }
            // Rede (só visual: o objecto que entra cai através dela).
            const int strands = 12;
            const float netDepth = 0.36f, netBottom = 0.15f;
            for (int i = 0; i < strands; i++)
            {
                float a = i * Mathf.PI * 2f / strands;
                var top = rimCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * HoopRules.RimRadius;
                var bottom = rimCenter + new Vector3(Mathf.Cos(a + 0.35f) * netBottom, -netDepth, Mathf.Sin(a + 0.35f) * netBottom);
                var strand = Shadowless(LowPoly.Part(LowPoly.Limb(4, 0.006f, 0.006f, Vector3.Distance(top, bottom)), "NetStrand", _carriage, top, Vector3.one, net));
                strand.transform.localRotation = Quaternion.FromToRotation(Vector3.down, (bottom - top).normalized);
            }
            foreach (var (y, r) in new[] { (-0.14f, 0.205f), (-netDepth, netBottom) })
            {
                var loop = Shadowless(LowPoly.Part(LowPoly.Ring(16, r, 0.012f, 0.012f), "NetLoop", _carriage, rimCenter + Vector3.up * y, Vector3.one, net));
                loop.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            _body = _carriage.gameObject.AddComponent<Rigidbody>();
            _body.isKinematic = true;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            _flash = new GameObject("HoopFlash").AddComponent<Light>();
            _flash.transform.SetParent(_carriage, false);
            _flash.transform.localPosition = rimCenter + new Vector3(0f, 0.25f, 0.25f);
            _flash.type = LightType.Point;
            _flash.color = ArtPalette.Mustard;
            _flash.range = 2.2f;
            _flash.intensity = 0f;
            _flash.shadows = LightShadows.None;
            _flash.enabled = false;

            _popup = VisualFactory.Text(_carriage, "", rimCenter + new Vector3(0f, 0.55f, 0.05f), 0.24f, ArtPalette.Mustard);
            _popup.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            PlaceCarriage(CarriageWorld(Clock()));
        }

        public void Bind(NetGame net)
        {
            if (_bound == net) return;
            if (_bound != null) _bound.Basket -= OnBasket;
            _bound = net;
            net.Basket += OnBasket;
        }

        static GameObject Shadowless(GameObject go)
        {
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        static GameObject Solid(Mesh mesh, string name, Transform parent, Vector3 position, Vector3 size, Material material)
        {
            var go = LowPoly.Part(mesh, name, parent, position, size, material);
            go.AddComponent<BoxCollider>();
            return go;
        }

        /// <summary>
        /// Relógio de rede visto por este peer, convertido para a base de tempo da física: o desvio (rede − local) é
        /// suavizado e os passos fixos usam Time.fixedTime + desvio (sem isto vários passos no mesmo frame davam a mesma posição).
        /// </summary>
        double Clock() => Time.timeAsDouble + _clockOffset;
        double FixedClock() => Time.fixedTimeAsDouble + _clockOffset;

        void UpdateClockOffset()
        {
            var net = NetGame.Instance;
            double target = net != null && net.IsSpawned ? net.NetworkTime - Time.timeAsDouble : 0.0;
            if (!_hasClock || System.Math.Abs(target - _clockOffset) > 0.25) _clockOffset = target;
            else _clockOffset += (target - _clockOffset) * 0.1;
            _hasClock = true;
        }

        Vector3 CarriageWorld(double clock) => transform.TransformPoint(new Vector3(HoopRules.Offset(clock), 0f, 0f));

        void PlaceCarriage(Vector3 world)
        {
            _carriage.position = world;
            if (_body != null) _body.position = world;
        }

        void Update()
        {
            if (_carriage == null) return;
            UpdateClockOffset();
            // Com a física parada (pausa solo: timeScale 0) não há passos fixos; a tabela segue o relógio na mesma.
            if (Time.timeScale <= 0f) PlaceCarriage(CarriageWorld(Clock()));
            UpdateFeedback();
        }

        void FixedUpdate()
        {
            if (_body == null) return;
            // Primeiro decide com o aro onde a física o deixou (o mesmo passo que moveu os objectos), depois avança-o.
            DetectBaskets();
            _body.MovePosition(CarriageWorld(FixedClock() + Time.fixedDeltaTime));
        }

        void DetectBaskets()
        {
            var net = NetGame.Instance;
            if (net == null || !net.IsSpawned || !net.IsServer) return;
            var rim = RimCenter;
            var live = ThrownProp.Live;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var prop = live[i];
                if (prop == null || prop.Body == null) continue;
                var rel = prop.Body.position - rim;
                if (prop.Track.Step(rel, prop.Body.linearVelocity.y, prop.Radius, net.IsServer))
                    net.ServerReportBasket(prop.ThrowId);
            }
        }

        void OnBasket(ulong thrower, int throwId, int credited)
        {
            BasketsSeen++;
            BasketShown?.Invoke(thrower, throwId, credited);
            var slot = _bound != null ? _bound.View.SlotForClient(thrower) : null;
            string who = slot != null ? slot.Profile.Name : "";
            _popup.text = credited > 0 ? "+" + credited + (who.Length > 0 ? "  " + who : "") : "CESTA!" + (who.Length > 0 ? "\n" + who : "");
            _popup.color = slot != null ? Color.Lerp(slot.Profile.Color, Color.white, 0.35f) : ArtPalette.Mustard;
            _popupAt = Time.unscaledTime;
            var sfx = _game != null ? _game.Sfx : null;
            if (sfx != null)
            {
                sfx.Play3D("pocketjoy", RimCenter, 0.75f, 1.1f);
                if (credited > 0) sfx.Play3D("coins", RimCenter, 0.6f, 1.15f);
            }
        }

        void UpdateFeedback()
        {
            float t = (Time.unscaledTime - _popupAt) / PopupSeconds;
            bool on = t >= 0f && t < 1f;
            _popup.gameObject.SetActive(on);
            _flash.enabled = on && t < 0.5f;
            if (!on) return;
            _popup.transform.localPosition = new Vector3(0f, RimY + 0.55f + t * 0.35f, RimOut + 0.05f);
            var c = _popup.color;
            c.a = t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f;
            _popup.color = c;
            _flash.intensity = Mathf.Lerp(3f, 0f, t * 2f);
            // O texto vira-se para a câmara local (legível de qualquer ponto da sala).
            var cam = _game != null && _game.Rig != null ? _game.Rig.Camera : null;
            if (cam != null)
            {
                var away = _popup.transform.position - cam.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.01f) _popup.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }
        }
    }
}
