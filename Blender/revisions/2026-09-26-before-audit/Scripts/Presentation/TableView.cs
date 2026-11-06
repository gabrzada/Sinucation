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
        public const float TopY = 0.40f;

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
            public Vector3 PocketFrom;
            public Vector3 PocketTo;
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
        void EnsureBall(int number)
        {
            if (_balls.ContainsKey(number)) return;
            if (!_ballMaterials.TryGetValue(number, out var mat))
            {
                mat = VisualFactory.ClearCoat(VisualFactory.BallTexture(number));
                _ballMaterials[number] = mat;
            }
            var go = VisualFactory.Primitive(PrimitiveType.Sphere, "Ball" + number, transform, Vector3.zero, Vector3.one * (_radius * 2f), mat);
            go.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            _balls[number] = new BallVisual { Transform = go.transform };
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
                        v.PocketAnim = t.gameObject.activeSelf ? 0f : 1f;
                        v.PocketFrom = t.position;
                        v.PocketTo = ToWorld(sim.Table.Pockets[Mathf.Clamp(ball.PocketIndex, 0, sim.Table.Pockets.Count - 1)].Center, _radius);
                    }
                    // Desliza até à boca da caçapa (a rolar) e cai lá para dentro.
                    v.PocketAnim += Time.deltaTime / 0.42f;
                    if (v.PocketAnim >= 1f) t.gameObject.SetActive(false);
                    else
                    {
                        t.gameObject.SetActive(true);
                        float k = v.PocketAnim;
                        float slide = Mathf.Clamp01(k / 0.45f);
                        Vector3 p = Vector3.Lerp(v.PocketFrom, v.PocketTo, 1f - (1f - slide) * (1f - slide));
                        float drop = Mathf.Clamp01((k - 0.4f) / 0.6f);
                        p.y -= drop * drop * 0.2f;
                        Vector3 step = p - t.position;
                        step.y = 0f;
                        if (step.sqrMagnitude > 1e-8f)
                            t.rotation = Quaternion.AngleAxis(step.magnitude / _radius * Mathf.Rad2Deg, Vector3.Cross(Vector3.up, step.normalized)) * t.rotation;
                        t.position = p;
                    }
                    v.LastPosition = ball.Position;
                    continue;
                }

                if (v.WasPocketed || !t.gameObject.activeSelf)
                {
                    v.WasPocketed = false;
                    v.LastPosition = ball.Position;
                    t.gameObject.SetActive(true);
                }

                Vector2 delta = ball.Position - v.LastPosition;
                float dist = delta.magnitude;
                if (dist > 1e-5f && dist < 0.5f)
                {
                    var axis = Vector3.Cross(Vector3.up, new Vector3(delta.x, 0f, delta.y) / dist);
                    t.rotation = Quaternion.AngleAxis(dist / _radius * Mathf.Rad2Deg, axis) * t.rotation;
                }
                v.LastPosition = ball.Position;
                t.position = ToWorld(ball.Position, _radius);
            }

            foreach (var kv in _balls)
                if (!seen.Contains(kv.Key)) kv.Value.Transform.gameObject.SetActive(false);

            var view = net.View;
            _headString.SetActive(view.InMatch && view.Phase == MatchPhase.AwaitShot && view.BallInHand && view.KitchenOnly);
        }
    }
}
