using System.Collections.Generic;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Objectos de distracção atirados pelos jogadores ([G]): bolas de papel, latas e patinhos. Usam PhysX
    /// local em cada cliente — são "social physics" puramente visual e nunca tocam na simulação de bilhar,
    /// que é separada e autoritativa.
    /// </summary>
    public sealed class ThrowablesView : MonoBehaviour
    {
        const int MaxProps = 24;
        const float Lifetime = 12f;

        readonly List<(GameObject go, float born)> _props = new List<(GameObject, float)>();
        GameBootstrap _game;
        Material[] _materials;
        int _counter;

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            _materials = new[]
            {
                VisualFactory.Lit(new Color(0.95f, 0.95f, 0.9f), 0.1f),
                VisualFactory.Lit(new Color(0.85f, 0.2f, 0.2f), 0.8f, 0.7f),
                VisualFactory.Lit(new Color(1f, 0.85f, 0.2f), 0.5f),
            };
        }

        public void Bind(NetGame net) => net.Thrown += OnThrown;

        void Update()
        {
            for (int i = _props.Count - 1; i >= 0; i--)
            {
                if (_props[i].go != null && Time.time - _props[i].born < Lifetime) continue;
                if (_props[i].go != null) Destroy(_props[i].go);
                _props.RemoveAt(i);
            }

            var net = NetGame.Instance;
            var keyboard = Keyboard.current;
            if (net == null || !net.IsSpawned || keyboard == null || _game.CursorFree || _game.Shot.UsesShotCamera) return;
            if (!keyboard.gKey.wasPressedThisFrame || _game.Rig.Camera == null) return;

            var cam = _game.Rig.Camera.transform;
            net.SendThrow(cam.position + cam.forward * 0.45f - cam.up * 0.1f, cam.forward * 7.5f + Vector3.up * 1.2f);
        }

        void OnThrown(ulong sender, Vector3 origin, Vector3 velocity)
        {
            if (_props.Count >= MaxProps)
            {
                Destroy(_props[0].go);
                _props.RemoveAt(0);
            }

            int kind = (int)((sender + (ulong)_counter++) % 3);
            GameObject go;
            switch (kind)
            {
                case 1:
                    go = VisualFactory.Primitive(PrimitiveType.Cylinder, "Can", transform, origin, new Vector3(0.07f, 0.06f, 0.07f), _materials[1]);
                    go.AddComponent<CapsuleCollider>();
                    break;
                case 2:
                    go = new GameObject("Duck");
                    go.transform.SetParent(transform, false);
                    go.transform.position = origin;
                    VisualFactory.Primitive(PrimitiveType.Sphere, "DuckBody", go.transform, Vector3.zero, new Vector3(0.12f, 0.09f, 0.14f), _materials[2]).transform.localPosition = Vector3.zero;
                    VisualFactory.Primitive(PrimitiveType.Sphere, "DuckHead", go.transform, new Vector3(0, 0.07f, 0.05f), Vector3.one * 0.07f, _materials[2]);
                    go.AddComponent<SphereCollider>().radius = 0.07f;
                    break;
                default:
                    go = VisualFactory.Primitive(PrimitiveType.Sphere, "PaperBall", transform, origin, Vector3.one * 0.09f, _materials[0]);
                    go.AddComponent<SphereCollider>();
                    break;
            }

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.5f;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<PropImpactSound>().Prefix = kind == 1 ? "metal" : kind == 2 ? "thud" : "cloth";
            _props.Add((go, Time.time));
        }
    }

    /// <summary>Som de impacto dos objectos atirados (lata metálica, pato de borracha, bola de papel).</summary>
    public sealed class PropImpactSound : MonoBehaviour
    {
        public string Prefix = "thud";
        float _last;

        void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < 0.6f || Time.time - _last < 0.12f) return;
            _last = Time.time;
            var game = GameBootstrap.Instance;
            if (game != null && game.Sfx != null)
                game.Sfx.Play3D(Prefix, transform.position, Mathf.Clamp01(speed / 6f) * 0.8f, Random.Range(0.9f, 1.15f) * (Prefix == "thud" ? 1.4f : 1f));
        }
    }
}
