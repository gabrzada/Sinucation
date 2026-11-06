using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// "Morte" sem gore: os personagens são aliens geométricos e rebentam em gosma verde fluorescente que
    /// salpica o cenário (chão, paredes, adereços). Puramente visual e local — cada cliente gera a sua gosma.
    /// </summary>
    public sealed class GooFX : MonoBehaviour
    {
        const int MaxSplats = 90;

        sealed class Splat
        {
            public Transform Transform;
            public float Born;
            public float Delay;
            public Vector3 TargetScale;
        }

        readonly List<Splat> _splats = new List<Splat>();
        Material _goo;
        ParticleSystem _drops;
        int _seed;

        public void Initialize()
        {
            _goo = VisualFactory.Lit(ArtPalette.Goo, 0.95f);
            VisualFactory.SetTransparent(_goo, true);
            VisualFactory.SetEmission(_goo, new Color(0.18f, 0.55f, 0.05f));

            var go = new GameObject("GooDrops");
            go.transform.SetParent(transform, false);
            _drops = go.AddComponent<ParticleSystem>();
            _drops.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _drops.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.11f);
            main.gravityModifier = 1.6f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _drops.emission;
            emission.enabled = false;
            var shape = _drops.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.15f;
            var collision = _drops.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.8f;
            collision.bounce = 0.1f;
            collision.lifetimeLoss = 0.4f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = LowPoly.Icosphere(1);
            renderer.sharedMaterial = _goo;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Explosão de gosma a partir de um ponto (cabeça do personagem).</summary>
        public void Burst(Vector3 origin, float power)
        {
            _drops.transform.position = origin;
            _drops.Emit(Mathf.RoundToInt(70 * power * GraphicsQuality.EffectsScale));

            var rng = new System.Random(++_seed * 7919);
            int rays = Mathf.RoundToInt(18 * power);
            for (int i = 0; i < rays; i++)
            {
                Vector3 dir = new Vector3((float)rng.NextDouble() * 2f - 1f, (float)rng.NextDouble() * 1.4f - 0.9f, (float)rng.NextDouble() * 2f - 1f).normalized;
                if (!Physics.Raycast(origin, dir, out var hit, 3.5f * power, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider.name == "TableCollider") continue;
                float size = Mathf.Lerp(0.25f, 0.6f, (float)rng.NextDouble()) * Mathf.Lerp(1f, 0.55f, hit.distance / 3.5f);
                AddSplat(hit.point, hit.normal, size, hit.distance * 0.12f, rng);
            }
            if (Physics.Raycast(origin, Vector3.down, out var floor, 3f, ~0, QueryTriggerInteraction.Ignore) && floor.collider.name != "TableCollider")
                AddSplat(floor.point, floor.normal, 1.1f * power, 0.15f, rng);
        }

        void AddSplat(Vector3 point, Vector3 normal, float size, float delay, System.Random rng)
        {
            if (_splats.Count >= MaxSplats)
            {
                Destroy(_splats[0].Transform.gameObject);
                _splats.RemoveAt(0);
            }
            var go = LowPoly.Part(LowPoly.Blob(rng.Next(12)), "Goo", transform, point + normal * 0.004f, Vector3.zero, _goo);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _splats.Add(new Splat { Transform = go.transform, Born = Time.time, Delay = delay, TargetScale = new Vector3(size, size * 0.6f, size) });
        }

        public void ClearAll()
        {
            foreach (var s in _splats)
                if (s.Transform != null) Destroy(s.Transform.gameObject);
            _splats.Clear();
        }

        void Update()
        {
            foreach (var s in _splats)
            {
                float t = Mathf.Clamp01((Time.time - s.Born - s.Delay) / 0.35f);
                float ease = 1f - Mathf.Pow(1f - t, 3f);
                s.Transform.localScale = s.TargetScale * ease;
            }
        }
    }
}
