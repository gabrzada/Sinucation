using System.Collections.Generic;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Câmara local em primeira pessoa. Os debuffs de câmara (DEBUFF-006..008) vêm do estado autoritativo
    /// mas só são renderizados aqui, no cliente do jogador afectado, enquanto é a vez dele.
    /// </summary>
    public sealed class FirstPersonRig : MonoBehaviour
    {
        static readonly List<ModifierInstance> s_scratch = new List<ModifierInstance>();

        GameBootstrap _game;
        Camera _camera;
        float _roll;
        float _shake;
        float _knockTilt;
        float _orbit;
        int _mode = -1;
        float _blend = 1f;
        Vector3 _fromPos, _lastPos;
        Quaternion _fromRot = Quaternion.identity, _lastRot = Quaternion.identity;
        float _bobPhase;

        public Camera Camera => _camera;
        public CameraEffectProfile ActiveProfile { get; private set; }

        public void Initialize(GameBootstrap game, Camera camera)
        {
            _game = game;
            _camera = camera;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 60f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.04f, 0.07f);
        }

        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        /// <summary>Queda de câmara quando o próprio jogador leva o disparo (desligada em modo conforto).</summary>
        public void KnockOver() => _knockTilt = 1f;

        /// <summary>Perfil de câmara do jogador local quando é ele a jogar e tem modificadores activos.</summary>
        public static CameraEffectProfile LocalShooterProfile(NetGame net, bool comfort)
        {
            if (net == null || !net.HasSnapshot) return new CameraEffectProfile();
            var view = net.View;
            var me = net.LocalPlayer;
            if (me == null || view.CurrentPlayer != me.Id) return new CameraEffectProfile();
            if (view.Phase != MatchPhase.AwaitShot && view.Phase != MatchPhase.Simulating) return new CameraEffectProfile();

            s_scratch.Clear();
            var catalog = NetGame.SharedCatalog;
            for (int i = 0; i < me.Mods.Count; i++)
            {
                var m = me.Mods[i];
                if (m.Def < 0 || m.Def >= catalog.Count) continue;
                s_scratch.Add(new ModifierInstance { Definition = catalog[m.Def], Intensity = m.Intensity, InstanceId = i });
            }
            return CameraModifierPipeline.Build(s_scratch, comfort);
        }

        void LateUpdate()
        {
            if (_camera == null) return;
            float dt = Time.unscaledDeltaTime;
            var net = NetGame.Instance;
            var local = NetPlayer.Local;
            var shot = _game.Shot;

            Vector3 pos;
            Quaternion rot;
            int mode;
            if (shot != null && shot.UsesShotCamera)
            {
                mode = 1;
                pos = shot.CameraPosition;
                rot = shot.CameraRotation;
            }
            else if (_game.Spectator != null && _game.Spectator.TryGetPose(out pos, out rot))
            {
                mode = 2;
                // Espectador: câmara em terceira pessoa / vista da mesa.
            }
            else if (local != null)
            {
                mode = 3;
                pos = local.EyePosition;
                rot = Quaternion.Euler(local.Pitch, local.Yaw, 0f);
                // Balanço suave da cabeça ao ritmo dos passos + afundar na aterragem (desligado em modo conforto).
                var body = local.Visual;
                if (body != null && !_game.Settings.ComfortMode && !local.IsGhost)
                {
                    float w = body.WalkWeight;
                    float ph = body.WalkPhase;
                    pos += Vector3.up * (-Mathf.Abs(Mathf.Sin(ph)) * 0.022f * w + 0.011f * w) + rot * Vector3.right * (Mathf.Sin(ph) * 0.012f * w);
                    rot *= Quaternion.Euler(0f, 0f, Mathf.Sin(ph) * 0.6f * w);
                    pos += Vector3.up * Mathf.Min(0f, body.Squash) * 0.25f;
                }
            }
            else
            {
                mode = 0;
                // Menu: câmara de montra, com uma deriva lenta para a cena parecer viva.
                _orbit += dt;
                pos = GameBootstrap.MenuCameraPosition + new Vector3(Mathf.Sin(_orbit * 0.21f) * 0.12f, Mathf.Sin(_orbit * 0.33f) * 0.04f, 0f);
                rot = Quaternion.LookRotation(GameBootstrap.MenuCameraTarget - pos);
            }

            var profile = LocalShooterProfile(net, _game.Settings.ComfortMode);
            ActiveProfile = profile;
            _roll = Mathf.LerpAngle(_roll, profile.Roll, 1f - Mathf.Exp(-dt * 5f));
            rot *= Quaternion.Euler(0f, 0f, _roll);

            if (profile.Sway > 0f)
            {
                float t = Time.time;
                float nx = Mathf.PerlinNoise(t * 0.35f, 0.1f) * 2f - 1f;
                float ny = Mathf.PerlinNoise(0.7f, t * 0.3f) * 2f - 1f;
                pos += rot * new Vector3(nx, ny, 0f) * (0.03f * profile.Sway);
                rot *= Quaternion.Euler(ny * 3f * profile.Sway, nx * 4f * profile.Sway, Mathf.Sin(t * 0.8f) * 7f * profile.Sway);
            }

            bool comfort = _game.Settings.ComfortMode;
            if (_knockTilt > 0f)
            {
                _knockTilt = Mathf.MoveTowards(_knockTilt, 0f, dt * 0.7f);
                if (!comfort)
                {
                    float k = Mathf.Sin(_knockTilt * Mathf.PI);
                    rot *= Quaternion.Euler(-25f * k, 0f, 55f * k);
                    pos += Vector3.down * 0.5f * k;
                }
            }

            if (_shake > 0.0001f)
            {
                if (!comfort) pos += Random.insideUnitSphere * _shake;
                _shake = Mathf.MoveTowards(_shake, 0f, dt * 0.2f);
            }

            // Transição suave quando muda o "dono" da câmara (primeira pessoa ↔ tacada ↔ espectador).
            if (mode != _mode)
            {
                if (_mode >= 0 && mode != 0)
                {
                    _fromPos = _lastPos;
                    _fromRot = _lastRot;
                    _blend = 0f;
                }
                _mode = mode;
            }
            if (_blend < 1f)
            {
                _blend = Mathf.Min(1f, _blend + dt / 0.35f);
                float k = Mathf.SmoothStep(0f, 1f, _blend);
                pos = Vector3.Lerp(_fromPos, pos, k);
                rot = Quaternion.Slerp(_fromRot, rot, k);
            }

            // Zoom para um objecto interactivo (máquina de sabotagem): sobrepõe-se à câmara normal.
            bool zoomed = _game.Interaction != null && _game.Interaction.TryGetCameraPose(pos, rot, out pos, out rot);
            float fov = _game.Settings.FieldOfView;
            if (zoomed) fov = Mathf.Lerp(fov, 50f, Mathf.SmoothStep(0f, 1f, _game.Interaction.ZoomBlend));

            bool shotMode = shot != null && shot.UsesShotCamera;
            float focus = shotMode ? Vector3.Distance(pos, new Vector3(0f, TableView.TopY, 0f)) : zoomed ? 0.6f : 3f;
            GraphicsQuality.SetFocus(focus, shotMode || zoomed);
            GraphicsQuality.SetExtraVignette(profile.Vignette);

            _camera.fieldOfView = fov;
            _camera.transform.SetPositionAndRotation(pos, rot);
            _lastPos = pos;
            _lastRot = rot;
        }
    }
}
