using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// "Morte" sem gore: a personagem rebenta num jacto estilizado de gosma vermelha — gotas (partículas com colisão),
    /// uma nuvem curta e manchas achatadas nas superfícies à volta (chão, paredes, adereços). As manchas crescem
    /// depressa, ficam um instante e desaparecem suavemente (~3 s no total). Nunca vão para o pano da mesa (a leitura
    /// da mesa não se perde) nem para personagens. Puramente visual: cada cliente gera a sua a partir do mesmo evento.
    /// </summary>
    public sealed class GooFX : MonoBehaviour
    {
        const int MaxSplats = 40;
        const float GrowSeconds = 0.22f, HoldSeconds = 1.7f, FadeSeconds = 1.1f;
        const float MaxReach = 2.2f;

        sealed class Splat
        {
            public Transform Transform;
            public Renderer Renderer;
            public float Born;
            public float Delay;
            public Vector3 TargetScale;
        }

        readonly List<Splat> _splats = new List<Splat>();
        readonly List<Vector3> _placed = new List<Vector3>();
        Material _goo;
        ParticleSystem _drops;
        ParticleSystem _mist;
        MaterialPropertyBlock _block;
        Color _color;
        int _seed;

        public void Initialize()
        {
            _color = ArtPalette.Goo;
            _goo = VisualFactory.Lit(_color, 0.92f);
            VisualFactory.SetTransparent(_goo, true);
            VisualFactory.SetEmission(_goo, new Color(0.3f, 0.01f, 0.02f));
            _block = new MaterialPropertyBlock();

            _drops = MakeSystem("GooDrops", LowPoly.Icosphere(1));
            var main = _drops.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.1f);
            main.gravityModifier = 1.7f;
            main.maxParticles = 260;
            var collision = _drops.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.85f;
            collision.bounce = 0.05f;
            collision.lifetimeLoss = 0.5f;
            var size = _drops.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            _mist = MakeSystem("GooMist", LowPoly.Icosphere(1));
            var mm = _mist.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            mm.gravityModifier = 0.2f;
            mm.maxParticles = 40;
            var ms = _mist.sizeOverLifetime;
            ms.enabled = true;
            ms.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.4f));
            var mc = _mist.colorOverLifetime;
            mc.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            mc.color = g;
        }

        ParticleSystem MakeSystem(string systemName, Mesh mesh)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.sharedMaterial = _goo;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return ps;
        }

        /// <summary>Jacto de gosma a partir de um ponto (cabeça). <paramref name="ignore"/>: o próprio boneco.</summary>
        public void Burst(Vector3 origin, float power, Transform ignore = null)
        {
            float fx = Mathf.Max(0.35f, GraphicsQuality.EffectsScale);
            _drops.transform.position = origin;
            _drops.Emit(Mathf.RoundToInt(60 * power * fx));
            _mist.transform.position = origin;
            _mist.Emit(Mathf.RoundToInt(10 * power));

            var rng = new System.Random(++_seed * 7919);
            int rays = Mathf.RoundToInt(16 * power * Mathf.Clamp(fx, 0.5f, 1f));
            for (int i = 0; i < rays; i++)
            {
                Vector3 dir = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 1.2f - 0.95f, (float)rng.NextDouble() * 2f - 1f).normalized;
                if (!Physics.Raycast(origin, dir, out var hit, MaxReach * power, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore)) continue;
                if (!Accepts(hit, ignore)) continue;
                float size = Mathf.Lerp(0.22f, 0.5f, (float)rng.NextDouble()) * Mathf.Lerp(1f, 0.55f, hit.distance / MaxReach);
                AddSplat(hit.point, hit.normal, size, hit.distance * 0.1f, rng);
            }
            if (Physics.Raycast(origin, Vector3.down, out var floor, 3f, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore) && Accepts(floor, ignore))
                AddSplat(floor.point, floor.normal, 0.95f * power, 0.12f, rng);
        }

        /// <summary>Só superfícies do cenário: nem o pano da mesa, nem personagens (jogadores, bots, ragdolls).</summary>
        static bool Accepts(RaycastHit hit, Transform ignore)
        {
            var c = hit.collider;
            if (c == null || c.name == "TableCollider") return false;
            if (c is CharacterController || c is CapsuleCollider || c.attachedRigidbody != null) return false;
            if (ignore != null && c.transform.IsChildOf(ignore)) return false;
            // Tampo da mesa (qualquer collider à altura do pano, por cima da mesa): a mesa fica sempre legível.
            if (hit.normal.y > 0.7f && Mathf.Abs(hit.point.y - TableView.TopY) < 0.05f && Mathf.Abs(hit.point.x) < 1.4f && Mathf.Abs(hit.point.z) < 0.9f) return false;
            return true;
        }

        void AddSplat(Vector3 point, Vector3 normal, float size, float delay, System.Random rng)
        {
            // Manchas demasiado próximas somam-se numa só (não empilham quads a piscar uns sobre os outros).
            foreach (var p in _placed)
                if ((p - point).sqrMagnitude < size * size * 0.09f) return;
            if (_splats.Count >= MaxSplats) RemoveAt(0);
            // Afastada da superfície por uma fracção do tamanho e ligeiramente elevada: sem z-fighting nem atravessar.
            var go = LowPoly.Part(LowPoly.Blob(rng.Next(12)), "Goo", transform, point + normal * 0.006f, Vector3.zero, _goo);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _splats.Add(new Splat { Transform = go.transform, Renderer = renderer, Born = Time.time, Delay = delay, TargetScale = new Vector3(size, size * 0.35f, size) });
            _placed.Add(point);
        }

        void RemoveAt(int index)
        {
            if (_splats[index].Transform != null) Destroy(_splats[index].Transform.gameObject);
            _splats.RemoveAt(index);
            _placed.RemoveAt(index);
        }

        public void ClearAll()
        {
            for (int i = _splats.Count - 1; i >= 0; i--) RemoveAt(i);
            _drops.Clear();
            _mist.Clear();
        }

        void Update()
        {
            float now = Time.time;
            for (int i = _splats.Count - 1; i >= 0; i--)
            {
                var s = _splats[i];
                float age = now - s.Born - s.Delay;
                if (age > GrowSeconds + HoldSeconds + FadeSeconds || s.Transform == null)
                {
                    RemoveAt(i);
                    continue;
                }
                float grow = Mathf.Clamp01(age / GrowSeconds);
                float ease = 1f - Mathf.Pow(1f - grow, 3f);
                float fade = Mathf.Clamp01((age - GrowSeconds - HoldSeconds) / FadeSeconds);
                // Desaparece encolhendo um pouco e ficando transparente (sem "pop").
                s.Transform.localScale = s.TargetScale * ease * Mathf.Lerp(1f, 0.75f, fade);
                var c = _color;
                c.a *= 1f - fade * fade;
                s.Renderer.GetPropertyBlock(_block);
                _block.SetColor("_BaseColor", c);
                s.Renderer.SetPropertyBlock(_block);
            }
        }
    }
}
