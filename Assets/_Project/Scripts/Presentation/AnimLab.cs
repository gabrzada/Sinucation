using System.IO;
using FriendSlop.Core;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Laboratório de animação (testes automáticos, -fsAnimLab &lt;pasta&gt;): um boneco faz um percurso guiado —
    /// parado, andar, travar, rodar no lugar, correr, saltar, tacada com taco, roleta, festejar, emotes, andar de lado,
    /// queda e fantasma — filmado por duas câmaras (lado e frente) e gravado fotograma a fotograma com tempo fixo
    /// (20 fps). Serve para rever o ciclo de passo e as transições sem depender de jogadores nem da rede.
    /// </summary>
    public sealed class AnimLab : MonoBehaviour
    {
        const float Fps = 20f;
        // Palco limpo fora da sala (sem adereços à frente das câmaras).
        const float LaneZ = -40f;

        string _dir;
        int _frame;
        float _t;
        Transform _holder;
        AvatarVisual _avatar;
        Camera _side, _front;
        Transform _cue;
        Transform _labTable, _labBall;
        Vector3 _pos;
        float _speed;
        float _lookYaw = 90f;
        Vector3 _sideCam, _frontCam;
        bool _ready;

        /// <summary>Só o revólver (-fsGunLab): apanhar, subir à têmpora, segurar, disparar, alívio e apontar.</summary>
        public static bool GunOnly;
        RevolverModel _gun;

        public static void Launch(string dir)
        {
            var go = new GameObject("AnimLab");
            go.AddComponent<AnimLab>()._dir = dir;
        }

        void UpdateGunLab(float dt)
        {
            if (_gun == null)
            {
                _gun = RevolverModel.Build(null);
                _holder.position = new Vector3(0f, 0f, LaneZ);
            }
            float t = _t;
            var stage = t < 1f ? GunStage.None : t < 1.8f ? GunStage.Reach : t < 2.8f ? GunStage.Raise : t < 5f ? GunStage.Hold : t < 5.3f ? GunStage.Fire : t < 6.5f ? GunStage.Relief : GunStage.Aim;
            float k = stage == GunStage.Raise ? (t - 1.8f) : stage == GunStage.Hold ? (t - 2.8f) / 2.2f : stage == GunStage.Fire ? (t - 5f) / 0.3f : stage == GunStage.Relief ? (t - 5.3f) / 1.2f : 0f;
            var pose = stage == GunStage.Aim || stage == GunStage.None ? AvatarPose.Idle : AvatarPose.Risk;
            _holder.rotation = Quaternion.Euler(0f, 180f + (stage == GunStage.Aim ? Mathf.Sin(t * 1.5f) * 25f : 0f), 0f);
            Vector3 reach = _holder.position + _holder.forward * 0.5f + _holder.right * 0.3f + Vector3.up * 1.1f;
            _avatar.SetGun(stage, k, _holder.forward, reach);
            _avatar.Animate(dt, pose, 5f, false);
            bool held = _avatar.TryGetHeldGun(out var p, out var r);
            _gun.Root.gameObject.SetActive(held || stage == GunStage.Reach);
            if (held)
            {
                _gun.Root.SetPositionAndRotation(p, r);
                _gun.Root.localScale = Vector3.one * AvatarVisual.HeldGunScale;
            }
            else
            {
                _gun.Root.position = reach;
                _gun.Root.localScale = Vector3.one * AvatarVisual.HeldGunScale;
            }
            // Duas câmaras: frente-direita (lado do revólver) e perfil direito, perto da cabeça.
            Vector3 head = _holder.position + Vector3.up * 1.05f;
            _side.transform.position = head + _holder.right * 1.25f + _holder.forward * 0.25f + Vector3.up * 0.1f;
            _side.transform.rotation = Quaternion.LookRotation(head - _side.transform.position);
            _front.transform.position = head + _holder.forward * 1.3f + _holder.right * 0.8f + Vector3.up * 0.15f;
            _front.transform.rotation = Quaternion.LookRotation(head - _front.transform.position);
        }

        void Start()
        {
            var game = GameBootstrap.Instance;
            Directory.CreateDirectory(_dir);
            Time.captureFramerate = (int)Fps;
            HudView.DebugHideAll = true;
            if (NpcAvatars.Instance != null) NpcAvatars.Instance.gameObject.SetActive(false);
            if (game != null && game.Rig != null && game.Rig.Camera != null) game.Rig.Camera.enabled = false;

            _side = MakeCamera("LabSide", new Rect(0f, 0f, 0.5f, 1f));
            _front = MakeCamera("LabFront", new Rect(0.5f, 0f, 0.5f, 1f));

            // Chão com a mesma textura de betão e luz de estúdio (chave + recorte).
            var floorMat = VisualFactory.Textured(TextureFactory.Concrete(), new Vector2(4f, 4f), 0.42f, 0.05f, 0.35f);
            VisualFactory.Primitive(PrimitiveType.Cube, "LabFloor", null, new Vector3(0f, -0.05f, LaneZ), new Vector3(14f, 0.1f, 10f), floorMat);
            var key = new GameObject("LabKey").AddComponent<Light>();
            key.type = LightType.Spot;
            key.spotAngle = 90f;
            key.range = 20f;
            key.intensity = 30f;
            key.shadows = LightShadows.Soft;
            key.transform.position = new Vector3(0f, 6f, LaneZ - 4f);
            key.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -6f, 4f));
            var fill = new GameObject("LabFill").AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 14f;
            fill.intensity = 6f;
            fill.color = new Color(1f, 0.85f, 0.7f);
            fill.transform.position = new Vector3(3f, 3f, LaneZ + 3f);

            _pos = new Vector3(-2.6f, 0f, LaneZ);
            _holder = new GameObject("LabActor").transform;
            _holder.position = _pos;
            _holder.rotation = Quaternion.Euler(0f, _lookYaw, 0f);
            var profile = game != null ? game.LocalProfile : new PlayerProfile("Lab", Archetype.Square, ArtPalette.Heads[2]);
            _avatar = AvatarVisual.Create(_holder, profile);

            // Taco simples para a parte da tacada.
            _cue = VisualFactory.Primitive(PrimitiveType.Cylinder, "LabCue", null, Vector3.zero, new Vector3(0.018f, 0.68f, 0.018f),
                VisualFactory.Lit(new Color(0.86f, 0.72f, 0.48f), 0.6f)).transform;
            _cue.gameObject.SetActive(false);
            // Tampo de mesa (à altura do jogo) e branca, só durante a tacada.
            _labTable = VisualFactory.Primitive(PrimitiveType.Cube, "LabTable", null, Vector3.zero, new Vector3(1.2f, TableView.TopY, 2.2f),
                VisualFactory.Lit(new Color(0.08f, 0.5f, 0.4f), 0.2f)).transform;
            _labBall = VisualFactory.Primitive(PrimitiveType.Sphere, "LabBall", null, Vector3.zero, Vector3.one * 0.057f, VisualFactory.Lit(Color.white, 0.8f)).transform;
            _labTable.gameObject.SetActive(false);
            _labBall.gameObject.SetActive(false);
            _sideCam = _pos + new Vector3(0f, 0.72f, -3f);
            _frontCam = _pos + new Vector3(2.6f, 0.72f, 0f);
            _ready = true;
        }

        static Camera MakeCamera(string name, Rect rect)
        {
            var go = new GameObject(name);
            var cam = go.AddComponent<Camera>();
            cam.rect = rect;
            cam.fieldOfView = 34f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.04f, 0.07f);
            return cam;
        }

        /// <summary>Velocidade-alvo e olhar ao longo do guião.</summary>
        void Script(float t, out float targetSpeed, out float moveYaw, out AvatarPose pose, out float y)
        {
            targetSpeed = 0f;
            moveYaw = 90f;
            pose = AvatarPose.Idle;
            y = 0f;
            if (t < 1.2f) return;                                   // parado
            if (t < 5.0f && _pos.x < 1.4f) { targetSpeed = 1.4f; return; } // andar para +x
            if (t < 5.6f) return;                                   // travar/parar
            if (t < 6.8f) { _lookYaw = -90f; return; }              // rodar no lugar (câmara vira)
            if (t < 8.8f && _pos.x > -1.6f) { targetSpeed = 3.4f; moveYaw = -90f; return; } // correr para −x
            if (t < 8.8f) return;
            if (t < 10.4f)
            {                                                       // dois saltos
                float u = (t - 8.8f) % 0.8f;
                y = Mathf.Max(0f, 3.6f * u - 7f * u * u);
                return;
            }
            if (t < 11.2f) { _lookYaw = 90f; return; }               // vira para a mesa imaginária
            if (t < 14.4f) { pose = AvatarPose.Aiming; return; }
            if (t < 15.8f) { pose = AvatarPose.Risk; return; }
            if (t < 17.2f) { pose = AvatarPose.Celebrate; return; }
            if (t < 21.2f) return;                                  // emotes
            if (t < 23.2f) { _lookYaw = 180f; targetSpeed = 1.2f; moveYaw = 90f; return; } // andar de lado
            if (t < 23.6f) { _lookYaw = 90f; return; }
        }

        void Update()
        {
            if (!_ready) return;
            float dt = 1f / Fps;
            _t += dt;
            if (GunOnly)
            {
                UpdateGunLab(dt);
                return;
            }

            Script(_t, out float targetSpeed, out float moveYaw, out var pose, out float y);
            float accel = targetSpeed > _speed ? 3.5f : 4.5f;
            _speed = Mathf.MoveTowards(_speed, targetSpeed, accel * dt);
            var dir = Quaternion.Euler(0f, moveYaw, 0f) * Vector3.forward;
            _pos += dir * _speed * dt;
            _holder.position = new Vector3(_pos.x, y, _pos.z);
            _holder.rotation = Quaternion.Euler(0f, _lookYaw, 0f);

            if (Mathf.Abs(_t - 17.2f) < dt * 0.5f) _avatar.PlayEmote(0);
            if (Mathf.Abs(_t - 19.2f) < dt * 0.5f) _avatar.PlayEmote(2);
            if (Mathf.Abs(_t - 23.8f) < dt * 0.5f) _avatar.Knockdown();
            _avatar.SetGhost(_t > 26.4f);

            // Taco durante a tacada: puxa atrás, pausa, avança (follow-through).
            bool aiming = pose == AvatarPose.Aiming;
            _cue.gameObject.SetActive(aiming);
            _labTable.gameObject.SetActive(aiming);
            _labBall.gameObject.SetActive(aiming);
            if (aiming)
            {
                var fwd = _holder.forward;
                Vector3 right = _holder.right;
                Vector3 tilt = Quaternion.AngleAxis(8f, right) * fwd;
                float u = _t - 11.2f;
                float pull = u < 2.4f ? Mathf.PingPong(u * 0.5f, 0.22f) : Mathf.Max(-0.16f, 0.22f - (u - 2.4f) * 2.5f);
                Vector3 ball = _holder.position + fwd * 0.8f + right * ShotMath.StanceSide + Vector3.up * (TableView.TopY + 0.029f);
                Vector3 tip0 = ball - tilt * 0.04f;
                Vector3 tip = tip0 - tilt * pull;
                float length = Mathf.Clamp(Vector3.Dot(_avatar.RightShoulderPosition - tip0, -tilt) + 0.2f + AvatarVisual.CueGripFromButt, 0.75f, 2.4f);
                _labTable.position = _holder.position + fwd * (0.23f + 1.1f) + Vector3.up * (TableView.TopY * 0.5f);
                _labTable.rotation = Quaternion.LookRotation(fwd);
                _labBall.position = ball;
                Vector3 bridge = ball - fwd * 0.18f;
                bridge.y = TableView.TopY + 0.035f;
                _avatar.SetCue(tip, tilt, length, bridge);
                _cue.localScale = new Vector3(0.018f, length * 0.5f, 0.018f);
                _cue.position = tip - tilt * (length * 0.5f);
                _cue.rotation = Quaternion.LookRotation(tilt) * Quaternion.Euler(90f, 0f, 0f);
            }

            _avatar.Animate(dt, pose, 10f, false);

            // Câmaras: lado (a acompanhar em X) e frente (à frente do boneco).
            Vector3 focus = new Vector3(_holder.position.x, 0.72f, LaneZ);
            _sideCam = Vector3.Lerp(_sideCam, focus + new Vector3(0f, 0.1f, -3.6f), 0.15f);
            _side.transform.position = _sideCam;
            _side.transform.rotation = Quaternion.LookRotation(focus - _sideCam);
            var yawRot = Quaternion.Euler(0f, _lookYaw, 0f);
            Vector3 frontWanted = focus + yawRot * Vector3.forward * 3.3f + Vector3.up * 0.1f;
            Vector3 frontLook = focus;
            if (pose == AvatarPose.Aiming)
            {
                // Mesmo ângulo das fotos de referência: à frente e à direita do jogador, um pouco de cima.
                // Três ângulos: frente-direita (como as fotos), de cima e pelo lado direito.
                float u = _t - 11.2f;
                if (u < 1.1f)
                {
                    frontWanted = _holder.position + yawRot * new Vector3(1.3f, 1.05f, 2.1f);
                    frontLook = _holder.position + yawRot * new Vector3(0.1f, 0.45f, 0.35f);
                }
                else if (u < 2.2f)
                {
                    frontWanted = _holder.position + yawRot * new Vector3(0.05f, 2.6f, 0.25f);
                    frontLook = _holder.position + yawRot * new Vector3(0.05f, 0.4f, 0.26f);
                }
                else
                {
                    frontWanted = _holder.position + yawRot * new Vector3(1.9f, 0.75f, -0.35f);
                    frontLook = _holder.position + yawRot * new Vector3(0.1f, 0.5f, 0.15f);
                }
                _frontCam = frontWanted;
            }
            _frontCam = Vector3.Lerp(_frontCam, frontWanted, pose == AvatarPose.Aiming ? 0.3f : 0.12f);
            _front.transform.position = _frontCam;
            _front.transform.rotation = Quaternion.LookRotation(frontLook - _frontCam);
        }

        void LateUpdate()
        {
            if (!_ready) return;
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, "lab_" + _frame.ToString("0000") + ".png"));
            _frame++;
            if (_t > (GunOnly ? 8.5f : 28.5f)) Application.Quit();
        }
    }
}
