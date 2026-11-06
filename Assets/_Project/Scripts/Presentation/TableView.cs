using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Mesa procedural e apresentação das bolas a partir do espelho replicado (NetGame.Mirror).
    /// Nunca simula nem decide nada: só desenha o estado autoritativo.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        /// <summary>
        /// Altura do pano à escala dos bonecos: como numa mesa real (0,8 m para pessoas), fica à altura da articulação
        /// da anca — assim o boneco debruça-se por cima do taco com a mão de trás junto à anca, como um jogador a sério.
        /// </summary>
        public const float TopY = 0.62f;

        readonly Dictionary<int, BallVisual> _balls = new Dictionary<int, BallVisual>();
        readonly Dictionary<int, Material> _ballMaterials = new Dictionary<int, Material>();
        TableGeometry _table;
        float _radius;
        GameObject _headString;

        sealed class BallVisual
        {
            public Transform Transform;
            public Vector2 LastPosition;
            public bool WasPocketed;
            public float PocketAnim;
            public Vector3 PocketTo;
            public float PocketHole;
            /// <summary>Velocidade de mundo suavizada enquanto rola (a bola entra na caçapa com ela).</summary>
            public Vector3 Velocity;
            public Vector3 PocketVelocity;
        }

        public static Vector3 ToWorld(Vector2 tablePoint, float height) => new Vector3(tablePoint.x, TopY + height, tablePoint.y);
        public static Vector2 ToTable(Vector3 world) => new Vector2(world.x, world.z);

        public TableGeometry Geometry => _table;
        public float BallRadius => _radius;

        public void Initialize(BilliardsPhysicsSettings settings)
        {
            _table = new TableGeometry(settings);
            _radius = settings.BallRadius;
            BuildTable();
        }

        void BuildTable()
        {
            float hl = _table.HalfLength, hw = _table.HalfWidth;
            var felt = VisualFactory.Textured(TextureFactory.Felt(), new Vector2(6f, 3f), 0.05f, 0f, 0.5f, new Color(0.78f, 0.78f, 0.78f));
            var cushionMat = VisualFactory.Textured(TextureFactory.Felt(), new Vector2(8f, 1f), 0.12f, 0f, 0.6f, new Color(0.85f, 0.85f, 0.85f));
            var rail = VisualFactory.Textured(TextureFactory.Wood(), new Vector2(4f, 0.4f), 0.6f, 0f, 0.5f, new Color(0.62f, 0.36f, 0.22f));
            var body = VisualFactory.Lit(ArtPalette.Hex(0x2A4566), 0.45f, 0.2f);
            var caps = VisualFactory.Textured(TextureFactory.WornMustard(), new Vector2(3f, 3f), 0.55f, 0.35f, 1f);
            var dark = VisualFactory.Lit(new Color(0.015f, 0.015f, 0.02f), 0f);
            var lineMat = VisualFactory.Lit(new Color(0.85f, 0.95f, 0.9f), 0f);
            var diamond = VisualFactory.Lit(ArtPalette.Ivory, 0.9f);
            var rivet = VisualFactory.Lit(ArtPalette.IvoryShade, 0.8f, 0.8f);
            var box = LowPoly.BevelBox(0.12f);

            var root = transform;
            VisualFactory.Primitive(PrimitiveType.Cube, "Felt", root, new Vector3(0, TopY - 0.02f, 0), new Vector3(hl * 2f + 0.02f, 0.04f, hw * 2f + 0.02f), felt);

            // Mesa modelada no Blender (Blender/table.py): tabelas, caçapas recortadas, corpo, pés e borrachas.
            var frame = FsModels.Get("table", "TableFrame");
            if (frame != null)
            {
                Material Resolve(string name)
                {
                    switch (name)
                    {
                        case "railMustard": return caps;
                        case "navy": return body;
                        case "suitDark": return VisualFactory.Textured(TextureFactory.MetalPanel(), new Vector2(1f, 1f), 0.35f, 0.3f, 0.6f, new Color(0.45f, 0.5f, 0.6f));
                        case "mustard": return VisualFactory.Lit(ArtPalette.Mustard, 0.55f, 0.3f);
                        case "steel": return rivet;
                        case "ivory": return diamond;
                        case "cushion": return VisualFactory.Textured(TextureFactory.Felt(), new Vector2(2.7f, 2.7f), 0.12f, 0f, 0.6f, new Color(0.85f, 0.85f, 0.85f));
                        case "dark": return dark;
                        default: return body;
                    }
                }
                var cache = new Dictionary<string, Material>();
                Material Cached(string name)
                {
                    if (!cache.TryGetValue(name, out var m)) cache[name] = m = Resolve(name);
                    return m;
                }
                FsModels.Spawn(frame, root, Vector3.zero, Quaternion.identity, Vector3.one, Cached);
                var cushionModel = FsModels.Get("table", "Cushions");
                if (cushionModel != null) FsModels.Spawn(cushionModel, root, Vector3.zero, Quaternion.identity, Vector3.one, Cached);
                BuildJaws(root, Cached("cushion"));
                VisualFactory.Primitive(PrimitiveType.Cylinder, "FootSpot", root, ToWorld(_table.FootSpot, 0.0005f), new Vector3(0.012f, 0.0005f, 0.012f), lineMat);
                _headString = VisualFactory.Primitive(PrimitiveType.Cube, "HeadString", root, ToWorld(new Vector2(_table.HeadStringX, 0), 0.0005f), new Vector3(0.004f, 0.001f, hw * 2f), lineMat);
                _headString.SetActive(false);
                return;
            }

            const float cushionDepth = 0.045f, cushionHeight = 0.04f;
            foreach (var seg in _table.Cushions)
            {
                Vector2 mid = (seg.A + seg.B) * 0.5f;
                Vector2 along = seg.B - seg.A;
                bool horizontal = Mathf.Abs(along.x) > Mathf.Abs(along.y);
                Vector2 outward = horizontal ? new Vector2(0, Mathf.Sign(mid.y)) : new Vector2(Mathf.Sign(mid.x), 0);
                Vector2 center = mid + outward * (cushionDepth * 0.5f);
                var scale = horizontal ? new Vector3(along.magnitude, cushionHeight, cushionDepth) : new Vector3(cushionDepth, cushionHeight, along.magnitude);
                VisualFactory.Primitive(PrimitiveType.Cube, "Cushion", root, ToWorld(center, cushionHeight * 0.5f), scale, cushionMat);
            }
            BuildJaws(root, cushionMat);

            const float railWidth = 0.15f, railHeight = 0.08f;
            float railOffset = cushionDepth + railWidth * 0.5f;
            float totalLength = hl * 2f + 2f * (cushionDepth + railWidth);
            float railY = railHeight * 0.5f - 0.02f;
            LowPoly.Part(box, "Rail", root, ToWorld(new Vector2(0, hw + railOffset), railY), new Vector3(totalLength, railHeight, railWidth), rail);
            LowPoly.Part(box, "Rail", root, ToWorld(new Vector2(0, -hw - railOffset), railY), new Vector3(totalLength, railHeight, railWidth), rail);
            LowPoly.Part(box, "Rail", root, ToWorld(new Vector2(hl + railOffset, 0), railY), new Vector3(railWidth, railHeight, hw * 2f + 2f * cushionDepth), rail);
            LowPoly.Part(box, "Rail", root, ToWorld(new Vector2(-hl - railOffset, 0), railY), new Vector3(railWidth, railHeight, hw * 2f + 2f * cushionDepth), rail);

            // Cantoneiras mostarda gastas nos cantos e nas caçapas laterais (concept da arena).
            float capX = hl + 0.2f, capZ = hw + 0.2f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                    LowPoly.Part(box, "CornerCap", root, ToWorld(new Vector2(sx * capX, sz * capZ), railY + 0.005f), new Vector3(0.22f, railHeight + 0.02f, 0.22f), caps);
            foreach (var sz in new[] { -1f, 1f })
                LowPoly.Part(box, "SideCap", root, ToWorld(new Vector2(0f, sz * (hw + 0.19f)), railY + 0.005f), new Vector3(0.2f, railHeight + 0.02f, 0.1f), caps);

            // Rebites ao longo das tabelas.
            for (int i = 0; i < 14; i++)
            {
                float x = Mathf.Lerp(-hl + 0.12f, hl - 0.12f, i / 13f);
                if (Mathf.Abs(x) < 0.14f) continue;
                foreach (float side in new[] { -1f, 1f })
                    LowPoly.Part(LowPoly.Icosphere(0), "Rivet", root, ToWorld(new Vector2(x, side * (hw + railOffset + 0.05f)), railHeight - 0.018f), Vector3.one * 0.014f, rivet);
            }

            // Diamantes: referência de mira em primeira pessoa.
            float diamondY = railHeight - 0.018f;
            for (int i = 1; i < 8; i++)
            {
                if (i == 4) continue;
                float x = -hl + i * (hl * 2f / 8f);
                foreach (float side in new[] { -1f, 1f })
                    VisualFactory.Primitive(PrimitiveType.Cube, "Diamond", root, ToWorld(new Vector2(x, side * (hw + railOffset - 0.02f)), diamondY), new Vector3(0.02f, 0.004f, 0.02f), diamond)
                        .transform.localRotation = Quaternion.Euler(0, 45f, 0);
            }
            for (int i = 1; i < 4; i++)
            {
                float z = -hw + i * (hw * 2f / 4f);
                foreach (float side in new[] { -1f, 1f })
                    VisualFactory.Primitive(PrimitiveType.Cube, "Diamond", root, ToWorld(new Vector2(side * (hl + railOffset - 0.02f), z), diamondY), new Vector3(0.02f, 0.004f, 0.02f), diamond)
                        .transform.localRotation = Quaternion.Euler(0, 45f, 0);
            }

            foreach (var pocket in _table.Pockets)
            {
                float visualRadius = pocket.IsCorner ? 0.07f : 0.062f;
                var ring = LowPoly.Part(LowPoly.Ring(16, visualRadius, 0.016f, 0.008f), "PocketRim", root, ToWorld(pocket.Center, railHeight - 0.02f), Vector3.one, caps);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                VisualFactory.Primitive(PrimitiveType.Cylinder, "Pocket", root, ToWorld(pocket.Center, railHeight - 0.018f), new Vector3(visualRadius * 2f, 0.002f, visualRadius * 2f), dark);
                VisualFactory.Primitive(PrimitiveType.Cylinder, "PocketFloor", root, ToWorld(pocket.Center, 0.001f), new Vector3(visualRadius * 2f, 0.001f, visualRadius * 2f), dark);
                LowPoly.Part(LowPoly.Limb(10, visualRadius, visualRadius * 0.7f, 0.18f), "PocketNet", root, ToWorld(pocket.Center, -0.02f), Vector3.one, dark);
            }

            // Corpo e pernas robustas com pés mostarda.
            LowPoly.Part(box, "Apron", root, new Vector3(0, TopY - 0.1f, 0), new Vector3(hl * 2f + 0.36f, 0.18f, hw * 2f + 0.36f), body);
            LowPoly.Part(box, "ApronBand", root, new Vector3(0, TopY - 0.19f, 0), new Vector3(hl * 2f + 0.3f, 0.04f, hw * 2f + 0.3f), caps);
            float legX = hl - 0.12f, legZ = hw - 0.04f;
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    LowPoly.Part(ToyMeshes.RoundedBox(0.17f, TopY - 0.2f, 0.17f, 0.05f), "Leg", root, new Vector3(sx * legX, (TopY - 0.2f) * 0.5f + 0.08f, sz * legZ), Vector3.one, body);
                    LowPoly.Part(ToyMeshes.RoundedBox(0.24f, 0.1f, 0.24f, 0.04f), "Foot", root, new Vector3(sx * legX, 0.05f, sz * legZ), Vector3.one, caps);
                }

            VisualFactory.Primitive(PrimitiveType.Cylinder, "FootSpot", root, ToWorld(_table.FootSpot, 0.0005f), new Vector3(0.012f, 0.0005f, 0.012f), lineMat);
            _headString = VisualFactory.Primitive(PrimitiveType.Cube, "HeadString", root, ToWorld(new Vector2(_table.HeadStringX, 0), 0.0005f), new Vector3(0.004f, 0.001f, hw * 2f), lineMat);
            _headString.SetActive(false);
        }
        /// <summary>
        /// Bocas das caçapas (a física em TableGeometry.Jaws): borracha fina ao longo de cada boca, do lado de fora da
        /// passagem, para se ver onde a bola bate e treme antes de cair.
        /// </summary>
        void BuildJaws(Transform root, Material material)
        {
            const float thickness = 0.018f, height = 0.032f;
            var jaws = _table.Jaws;
            for (int i = 0; i < jaws.Count; i++)
            {
                var jaw = jaws[i];
                var partner = jaws[i ^ 1];
                Vector2 along = jaw.B - jaw.A, mid = (jaw.A + jaw.B) * 0.5f;
                Vector2 n = new Vector2(-along.y, along.x).normalized;
                if (Vector2.Dot(n, (partner.A + partner.B) * 0.5f - mid) > 0f) n = -n;
                var go = VisualFactory.Primitive(PrimitiveType.Cube, "PocketJaw", root, ToWorld(mid + n * (thickness * 0.5f), height * 0.5f), new Vector3(along.magnitude + 0.004f, height, thickness), material);
                go.transform.localRotation = Quaternion.Euler(0f, -Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg, 0f);
            }
        }

        void EnsureBall(int number)
        {
            if (_balls.ContainsKey(number)) return;
            if (!_ballMaterials.TryGetValue(number, out var mat))
            {
                mat = VisualFactory.ClearCoat(VisualFactory.BallTexture(number));
                _ballMaterials[number] = mat;
            }
            var go = VisualFactory.Primitive(PrimitiveType.Sphere, "Ball" + number, transform, Vector3.zero, Vector3.one * (_radius * 2f), mat);
            go.layer = GameplayCollision.BilliardBall;
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            _balls[number] = new BallVisual { Transform = go.transform };
        }

        /// <summary>
        /// Queda na caçapa: a bola rola até à boca com a velocidade que trazia, tomba quando o centro passa a borda,
        /// cai com gravidade e ressalta nas paredes do copo até desaparecer no escuro (sem buraco real no modelo,
        /// o disco escuro da caçapa tapa a parte que já desceu).
        /// </summary>
        void AnimatePocketing(BallVisual v, Transform t, float dt)
        {
            t.gameObject.SetActive(true);
            v.PocketAnim += dt;
            Vector3 p = t.position;
            Vector3 vel = v.PocketVelocity;
            Vector3 center = v.PocketTo;
            Vector3 radial = new Vector3(p.x - center.x, 0f, p.z - center.z);
            bool overHole = radial.magnitude < v.PocketHole + _radius * 0.3f;
            // Puxa ligeiramente para o centro (a boca da caçapa é inclinada) e, já por cima do buraco, cai.
            Vector3 pull = -radial.normalized * (overHole ? 1.5f : 4f);
            if (radial.sqrMagnitude > 1e-8f) vel += pull * dt;
            if (overHole || p.y < center.y - 0.001f) vel.y -= 5.5f * dt; // gravidade "de palco": a queda lê-se
            p += vel * dt;
            // Paredes do copo: ressalto amortecido (o "clac" contra o couro).
            radial = new Vector3(p.x - center.x, 0f, p.z - center.z);
            float wall = v.PocketHole;
            if (p.y < center.y - _radius * 0.5f && radial.magnitude > wall)
            {
                Vector3 n = radial.normalized;
                p = new Vector3(center.x, p.y, center.z) + n * wall;
                float into = Vector3.Dot(vel, n);
                if (into > 0f) vel -= n * into * 1.45f;
                vel.x *= 0.8f;
                vel.z *= 0.8f;
            }
            Vector3 step = p - t.position;
            step.y = 0f;
            if (step.sqrMagnitude > 1e-8f)
                t.rotation = Quaternion.AngleAxis(step.magnitude / _radius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * t.rotation;
            t.position = p;
            v.PocketVelocity = vel;
            // Já abaixo do disco escuro (ou demasiado tempo): acabou.
            if (p.y < center.y - _radius * 2.6f || v.PocketAnim > 1.2f) v.PocketAnim = 10f;
        }

        void LateUpdate()
        {
            var net = NetGame.Instance;
            if (net == null || !net.HasSnapshot || net.Mirror == null)
            {
                foreach (var kv in _balls) kv.Value.Transform.gameObject.SetActive(false);
                if (_headString != null) _headString.SetActive(false);
                return;
            }

            var sim = net.Mirror;
            var seen = new HashSet<int>();
            foreach (var ball in sim.Balls)
            {
                EnsureBall(ball.Number);
                seen.Add(ball.Number);
                var v = _balls[ball.Number];
                var t = v.Transform;

                if (ball.Pocketed)
                {
                    if (!v.WasPocketed)
                    {
                        v.WasPocketed = true;
                        v.PocketAnim = t.gameObject.activeSelf ? 0f : 10f;
                        var pocket = sim.Table.Pockets[Mathf.Clamp(ball.PocketIndex, 0, sim.Table.Pockets.Count - 1)];
                        v.PocketTo = ToWorld(pocket.Center, _radius);
                        v.PocketHole = Mathf.Max(pocket.Radius - _radius * 0.35f, _radius * 0.4f);
                        // Entra com a velocidade que trazia (mínimo 0,6 m/s em direcção à caçapa, nunca a fugir dela).
                        Vector3 toPocket = v.PocketTo - t.position;
                        toPocket.y = 0f;
                        Vector3 flat = new Vector3(v.Velocity.x, 0f, v.Velocity.z);
                        float speed = Mathf.Clamp(flat.magnitude, 0.6f, 3f);
                        Vector3 dir = toPocket.sqrMagnitude > 1e-6f ? toPocket.normalized : (flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward);
                        if (flat.sqrMagnitude > 1e-6f && Vector3.Dot(flat.normalized, dir) > 0f) dir = Vector3.Slerp(flat.normalized, dir, 0.5f).normalized;
                        v.PocketVelocity = dir * speed;
                    }
                    if (v.PocketAnim < 10f) AnimatePocketing(v, t, Mathf.Min(Time.deltaTime, 0.05f));
                    else t.gameObject.SetActive(false);
                    v.LastPosition = ball.Position;
                    continue;
                }

                if (v.WasPocketed || !t.gameObject.activeSelf)
                {
                    v.WasPocketed = false;
                    v.LastPosition = ball.Position;
                    t.gameObject.SetActive(true);
                    v.Velocity = Vector3.zero;
                }

                Vector2 delta = ball.Position - v.LastPosition;
                float dist = delta.magnitude;
                if (dist > 1e-5f && dist < 0.5f)
                {
                    var axis = Vector3.Cross(Vector3.up, new Vector3(delta.x, 0f, delta.y) / dist);
                    t.rotation = Quaternion.AngleAxis(dist / _radius * Mathf.Rad2Deg, axis) * t.rotation;
                }
                v.LastPosition = ball.Position;
                Vector3 next = ToWorld(ball.Position, _radius);
                if (Time.deltaTime > 1e-4f && t.gameObject.activeSelf)
                    v.Velocity = Vector3.Lerp(v.Velocity, (next - t.position) / Time.deltaTime, 0.5f);
                t.position = next;
            }

            foreach (var kv in _balls)
                if (!seen.Contains(kv.Key)) kv.Value.Transform.gameObject.SetActive(false);

            var view = net.View;
            _headString.SetActive(view.InMatch && view.Phase == MatchPhase.AwaitShot && view.BallInHand && view.KitchenOnly);
            UpdateLegalMarker(net, view);
        }

        // Realce da bola legal: anel no pano à volta dela e seta a flutuar por cima (visível de qualquer ponto da sala).
        // Com o Daltonismo na vez do próprio jogador o realce some: descobrir qual é a bola é a graça do debuff.
        GameObject _legalRing, _legalArrow;
        Renderer _legalRingRenderer, _legalArrowRenderer;
        Material _legalMat, _lastBallMat;

        void UpdateLegalMarker(NetGame net, MatchView view)
        {
            bool atRest = view.Phase == MatchPhase.RoundIntro || view.Phase == MatchPhase.AwaitShot || view.Phase == MatchPhase.ShotWindup || view.Phase == MatchPhase.ShotResult;
            bool colorBlind = FirstPersonRig.LocalShooterProfile(net, false).Deuteranopia > 0f;
            int lowest = view.InMatch && atRest && !colorBlind ? view.LowestBall : -1;
            BallVisual ball = null;
            bool show = lowest > 0 && _balls.TryGetValue(lowest, out ball) && ball.Transform.gameObject.activeSelf && !ball.WasPocketed;
            if (!show)
            {
                if (_legalRing != null) { _legalRing.SetActive(false); _legalArrow.SetActive(false); }
                return;
            }
            if (_legalRing == null)
            {
                _legalMat = VisualFactory.Emissive(new Color(1f, 0.78f, 0.2f), 2.2f);
                _lastBallMat = VisualFactory.Emissive(new Color(1f, 0.36f, 0.3f), 2.6f);
                _legalRing = LowPoly.Part(LowPoly.Ring(28, _radius * 1.55f, 0.0045f, 0.0025f), "LegalBallRing", transform, Vector3.zero, Vector3.one, _legalMat);
                _legalRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                _legalArrow = LowPoly.Part(LowPoly.Limb(10, 0.02f, 0.002f, 0.042f), "LegalBallArrow", transform, Vector3.zero, Vector3.one, _legalMat);
                _legalRingRenderer = _legalRing.GetComponent<Renderer>();
                _legalArrowRenderer = _legalArrow.GetComponent<Renderer>();
                foreach (var r in new[] { _legalRingRenderer, _legalArrowRenderer }) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // Última bola: coral e a pulsar mais (quem a meter numa tacada legal ganha a ronda).
            bool last = view.ObjectBallsOnTable == 1;
            var mat = last ? _lastBallMat : _legalMat;
            if (_legalRingRenderer.sharedMaterial != mat) { _legalRingRenderer.sharedMaterial = mat; _legalArrowRenderer.sharedMaterial = mat; }
            float t = Time.time;
            Vector3 p = ball.Transform.position;
            _legalRing.SetActive(true);
            _legalArrow.SetActive(true);
            _legalRing.transform.position = new Vector3(p.x, TopY + 0.0015f, p.z);
            _legalRing.transform.localScale = Vector3.one * (1f + (last ? 0.2f : 0.1f) * (0.5f + 0.5f * Mathf.Sin(t * (last ? 7f : 4f))));
            _legalArrow.transform.position = p + Vector3.up * (0.115f + 0.012f * Mathf.Sin(t * 3f));
            _legalArrow.transform.rotation = Quaternion.Euler(0f, t * 90f, 0f);
        }
    }
}
