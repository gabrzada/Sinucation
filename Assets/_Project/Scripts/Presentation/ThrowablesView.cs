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
                VisualFactory.Lit(new Color(0.67f, 0.69f, 0.72f), 0.65f, 0.8f),
                VisualFactory.Lit(new Color(0.035f, 0.04f, 0.05f), 0.32f),
                VisualFactory.Lit(new Color(0.92f, 0.9f, 0.79f), 0.35f),
                VisualFactory.Lit(ArtPalette.Mustard, 0.45f),
                VisualFactory.Lit(new Color(0.85f, 0.58f, 0.06f), 0.4f),
                VisualFactory.Lit(new Color(1f, 0.32f, 0.06f), 0.45f),
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

        Material ResolveThrowableMaterial(string name)
        {
            switch (name)
            {
                case "paper": return _materials[0];
                case "red": return _materials[1];
                case "duck": return _materials[2];
                case "steel": return _materials[3];
                case "dark": return _materials[4];
                case "ivory": return _materials[5];
                case "mustard": return _materials[6];
                case "duckShade": return _materials[7];
                case "beak": return _materials[8];
                default: return _materials[0];
            }
        }

        void OnThrown(ThrowInfo info)
        {
            ulong sender = info.Sender;
            var (origin, velocity) = CatchUp(info.Origin, info.Velocity, info.Age);
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

            // A malha é apenas visual: mantemos os colliders e as escalas das primitivas originais.
            var model = FsModels.Get("throwables", kind == 1 ? "Can" : kind == 2 ? "Duck" : "PaperBall");
            if (model != null)
            {
                foreach (var renderer in go.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
                var scale = go.transform.localScale;
                FsModels.Spawn(model, go.transform, Vector3.zero, Quaternion.identity,
                    new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z), ResolveThrowableMaterial);
            }

            go.layer = GameplayCollision.Throwable;
            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.15f;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.5f;
            body.linearVelocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<PropImpactSound>().Prefix = kind == 1 ? "metal" : kind == 2 ? "thud" : "cloth";
            var thrown = go.AddComponent<ThrownProp>();
            thrown.ThrowId = info.Id;
            thrown.Thrower = sender;
            thrown.Radius = kind == 2 ? 0.07f : kind == 1 ? 0.05f : 0.045f;
            thrown.Body = body;
            _props.Add((go, Time.time));
        }

        /// <summary>
        /// Adianta o objecto pelo voo que já decorreu no relógio de rede (balística simples até ao primeiro obstáculo):
        /// assim o objecto e a cesta móvel, que também segue o relógio de rede, ficam na mesma posição relativa em todos.
        /// </summary>
        static (Vector3 origin, Vector3 velocity) CatchUp(Vector3 origin, Vector3 velocity, float age)
        {
            if (age <= 0f) return (origin, velocity);
            var target = origin + velocity * age + 0.5f * age * age * Physics.gravity;
            var path = target - origin;
            if (Physics.Raycast(origin, path.normalized, out var hit, path.magnitude, GameplayCollision.WorldQueryMask & ~(1 << GameplayCollision.Player), QueryTriggerInteraction.Ignore))
                return (origin, velocity);
            return (target, velocity + Physics.gravity * age);
        }
    }

    /// <summary>Identidade de rede de um objecto atirado: o lançamento (id do anfitrião) e quem o atirou.</summary>
    public sealed class ThrownProp : MonoBehaviour
    {
        static readonly List<ThrownProp> LiveList = new List<ThrownProp>();
        public static IReadOnlyList<ThrownProp> Live => LiveList;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay() => LiveList.Clear();

        public int ThrowId;
        public ulong Thrower;
        public float Radius;
        public Rigidbody Body;
        /// <summary>Estado do detector da cesta (só avança no anfitrião).</summary>
        public FriendSlop.Core.RimTrack Track;

        void OnEnable() => LiveList.Add(this);
        void OnDisable() => LiveList.Remove(this);
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
