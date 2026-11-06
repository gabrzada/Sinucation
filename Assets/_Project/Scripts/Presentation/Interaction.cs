using System.Collections.Generic;
using FriendSlop.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FriendSlop.Presentation
{
    /// <summary>Objecto do mundo com que o jogador interage em primeira pessoa ([E]).</summary>
    public abstract class Interactable : MonoBehaviour
    {
        public virtual float Range => 2.4f;
        /// <summary>Ponto que o jogador tem de estar a olhar.</summary>
        public abstract Vector3 FocusPoint { get; }
        public abstract string Prompt { get; }
        public abstract bool CanUse(NetGame net);

        /// <summary>Se true, abrir faz zoom da câmara para <see cref="ZoomPose"/> e liberta o rato.</summary>
        public virtual bool Zooms => true;
        public virtual Pose ZoomPose => new Pose(transform.position, transform.rotation);

        /// <summary>Cantos do ecrã do objecto em mundo (inf. esq., sup. esq., sup. dir., inf. dir.) para desenhar a UI.</summary>
        public virtual Vector3[] ScreenCorners => null;

        public virtual void OnOpened() { }
        public virtual void OnClosed() { }
    }

    /// <summary>
    /// Escolhe o interactivo para onde o jogador local está a olhar, mostra o aviso e gere o zoom.
    /// Enquanto aberto, o movimento e o olhar ficam presos e o rato fica livre para a UI do objecto.
    /// </summary>
    public sealed class InteractionSystem : MonoBehaviour
    {
        /// <summary>Com 'Enter Play Mode Options' (sem recarregar o domínio) os estáticos sobrevivem entre Plays: repõe-os.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            All.Clear();
        }

        public static readonly List<Interactable> All = new List<Interactable>();

        GameBootstrap _game;
        float _zoomBlend;

        public Interactable Focused { get; private set; }
        public Interactable Active { get; private set; }
        public float ZoomBlend => _zoomBlend;

        public void Initialize(GameBootstrap game) => _game = game;

        public void Close()
        {
            if (Active == null) return;
            Active.OnClosed();
            Active = null;
            var local = NetPlayer.Local;
            if (local != null)
            {
                local.MovementLocked = false;
                local.LookLocked = false;
            }
        }

        /// <summary>Testes automáticos: abre um interactivo sem precisar de olhar para ele.</summary>
        public void DebugOpen(Interactable it)
        {
            if (it == null || Active == it) return;
            Close();
            Active = it;
            Active.OnOpened();
        }

        public bool TryGetCameraPose(Vector3 fpsPosition, Quaternion fpsRotation, out Vector3 position, out Quaternion rotation)
        {
            position = fpsPosition;
            rotation = fpsRotation;
            if (_zoomBlend <= 0.001f || Active == null || !Active.Zooms) return false;
            var target = Active.ZoomPose;
            float t = Mathf.SmoothStep(0f, 1f, _zoomBlend);
            position = Vector3.Lerp(fpsPosition, target.position, t);
            rotation = Quaternion.Slerp(fpsRotation, target.rotation, t);
            return true;
        }

        void Update()
        {
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            var cam = _game.Rig != null ? _game.Rig.Camera : null;
            Focused = null;

            if (Active != null && (net == null || local == null || !Active.CanUse(net))) Close();
            _zoomBlend = Mathf.MoveTowards(_zoomBlend, Active != null && Active.Zooms ? 1f : 0f, Time.unscaledDeltaTime * 3f);

            if (net == null || local == null || cam == null || _game.Paused || _game.MatchPaused) return;
            var keyboard = Keyboard.current;

            if (Active != null)
            {
                local.MovementLocked = true;
                local.LookLocked = true;
                if (keyboard != null && !_game.Hud.TextInputFocused && (keyboard.qKey.wasPressedThisFrame || (keyboard.eKey.wasPressedThisFrame && !_game.Hud.ChatOpen))) Close();
                return;
            }
            if (_game.Shot.UsesShotCamera || _game.CursorFree || (_game.Spectator != null && _game.Spectator.ControlsCamera)) return;

            float best = float.MaxValue;
            Vector3 eye = cam.transform.position;
            Vector3 forward = cam.transform.forward;
            foreach (var it in All)
            {
                if (it == null || !it.isActiveAndEnabled || !it.CanUse(net)) continue;
                Vector3 to = it.FocusPoint - eye;
                float dist = to.magnitude;
                if (dist > it.Range) continue;
                float angle = Vector3.Angle(forward, to);
                if (angle > 32f) continue;
                float score = angle + dist * 8f;
                if (score < best)
                {
                    best = score;
                    Focused = it;
                }
            }

            if (Focused != null && keyboard != null && keyboard.eKey.wasPressedThisFrame)
            {
                Active = Focused;
                Active.OnOpened();
                _game.Sfx.PlayUi("uiopen");
            }
        }
    }
}
