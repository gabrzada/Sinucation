using System.Collections.Generic;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Marcadores (botão do meio / roda premida): seta semitransparente na cor de quem marcou, a apontar para o sítio,
    /// com um anel achatado na superfície. Visível para toda a sala, dura PingDurationSeconds e desaparece a
    /// desvanecer. O anfitrião valida a recarga (NetGame.PingRpc); aqui só há a previsão local e o aviso discreto.
    /// </summary>
    public sealed class PingView : MonoBehaviour
    {
        const float FadeIn = 0.15f, FadeOut = 0.6f;

        public sealed class Marker
        {
            public ulong Sender;
            public string Name;
            public Color Color;
            public Vector3 Point;
            public float Born;
            public Transform Root;
            public Transform Arrow;
            public Renderer[] Renderers;
            public float Alpha;
        }

        GameBootstrap _game;
        NetGame _bound;
        readonly List<Marker> _markers = new List<Marker>();
        readonly RaycastHit[] _hits = new RaycastHit[12];
        MaterialPropertyBlock _block;
        Material _material;
        Mesh _cone, _ring;

        public IReadOnlyList<Marker> Markers => _markers;
        /// <summary>Até quando mostrar o aviso "marcador em recarga" e quantos segundos faltavam.</summary>
        public float CooldownNoticeUntil { get; private set; } = -10f;
        public float CooldownNoticeSeconds { get; private set; }

        public void Initialize(GameBootstrap game)
        {
            _game = game;
            _block = new MaterialPropertyBlock();
            _material = VisualFactory.Lit(Color.white, 0.4f);
            VisualFactory.SetTransparent(_material, true);
            VisualFactory.SetEmission(_material, new Color(0.25f, 0.25f, 0.25f));
            _cone = VisualFactory.ConeShared;
            _ring = LowPoly.Ring(28, 0.16f, 0.018f, 0.004f);
        }

        void Bind(NetGame net)
        {
            _bound = net;
            net.Ping += OnPing;
            net.PingRefused += remaining => Notice(remaining);
        }

        void Notice(float remaining)
        {
            CooldownNoticeSeconds = remaining;
            CooldownNoticeUntil = Time.unscaledTime + 1.1f;
        }

        void Update()
        {
            var net = NetGame.Instance;
            if (net != null && net != _bound && net.IsSpawned) Bind(net);
            UpdateInput(net);
            Animate();
        }

        void UpdateInput(NetGame net)
        {
            var mouse = Mouse.current;
            if (net == null || !net.HasSnapshot || mouse == null || !mouse.middleButton.wasPressedThisFrame) return;
            if (_game.Screen != AppScreen.Session || _game.CursorFree || _game.Paused || _game.MatchPaused || _game.Hud.ChatOpen) return;
            var cam = _game.Rig.Camera;
            if (cam == null) return;
            if (net.PingCooldownRemaining > 0f)
            {
                Notice(net.PingCooldownRemaining);
                _game.Sfx.PlayUi("uierror");
                return;
            }
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            int count = Physics.RaycastNonAlloc(ray, _hits, 40f, GameplayCollision.WorldQueryMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit? chosen = null;
            var local = NetPlayer.Local;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                if (h.collider == null || h.distance >= best) continue;
                if (local != null && h.collider.transform.IsChildOf(local.transform)) continue;
                best = h.distance;
                chosen = h;
            }
            if (chosen == null) return;
            var hit = chosen.Value;
            // Na mesa, o marcador fica no pano (plano das bolas), não enterrado na tabela.
            Vector3 point = hit.point;
            if (net.RequestPing(point, hit.normal)) _game.Sfx.PlayUi("uiselect");
        }

        void OnPing(ulong sender, Vector3 point, Vector3 normal)
        {
            var net = NetGame.Instance;
            for (int i = _markers.Count - 1; i >= 0; i--)
                if (_markers[i].Sender == sender) Remove(i);
            var slot = net != null ? net.View.SlotForClient(sender) : null;
            var color = slot != null ? slot.Profile.Color : new Color(0.8f, 0.85f, 0.95f);
            var root = new GameObject("Ping").transform;
            root.SetParent(transform, false);
            root.position = point + normal * 0.01f;
            root.rotation = Quaternion.FromToRotation(Vector3.up, normal);
            var ring = LowPoly.Part(_ring, "PingRing", root, Vector3.zero, Vector3.one, _material);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var arrowPivot = LowPoly.Pivot("PingArrow", root, Vector3.zero);
            // Seta a apontar para baixo, sempre na vertical (lê-se de qualquer ângulo), um pouco acima do ponto.
            var cone = LowPoly.Part(_cone, "Cone", arrowPivot, new Vector3(0f, 0.13f, 0f), new Vector3(0.08f, -0.14f, 0.08f), _material);
            var stem = LowPoly.Part(LowPoly.Limb(10, 0.016f, 0.016f, 0.12f), "Stem", arrowPivot, new Vector3(0f, 0.24f, 0f), Vector3.one, _material);
            var renderers = new[] { ring.GetComponent<Renderer>(), cone.GetComponent<Renderer>(), stem.GetComponent<Renderer>() };
            foreach (var r in renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _markers.Add(new Marker
            {
                Sender = sender, Name = slot != null ? slot.Profile.Name : "", Color = color, Point = point, Born = Time.unscaledTime,
                Root = root, Arrow = arrowPivot, Renderers = renderers
            });
            _game.Sfx.Play3D("uitick", point, 0.6f, 1.2f);
        }

        void Animate()
        {
            float now = Time.unscaledTime;
            float duration = NetGame.PingDurationSeconds;
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                var m = _markers[i];
                float age = now - m.Born;
                if (age >= duration || m.Root == null)
                {
                    Remove(i);
                    continue;
                }
                float a = Mathf.Clamp01(age / FadeIn) * Mathf.Clamp01((duration - age) / FadeOut);
                m.Alpha = a;
                float bob = Mathf.Sin(age * 5f) * 0.03f;
                m.Arrow.rotation = Quaternion.identity;
                m.Arrow.position = m.Point + Vector3.up * (0.05f + bob);
                float pulse = 1f + Mathf.Repeat(age * 1.4f, 1f) * 0.35f;
                m.Renderers[0].transform.localScale = Vector3.one * pulse;
                var c = m.Color;
                c.a = 0.42f * a;
                for (int r = 0; r < m.Renderers.Length; r++)
                {
                    var color = c;
                    if (r == 0) color.a *= 1.4f - (pulse - 1f) * 2f;
                    m.Renderers[r].GetPropertyBlock(_block);
                    _block.SetColor("_BaseColor", color);
                    m.Renderers[r].SetPropertyBlock(_block);
                }
            }
        }

        void Remove(int index)
        {
            if (_markers[index].Root != null) Destroy(_markers[index].Root.gameObject);
            _markers.RemoveAt(index);
        }
    }
}
