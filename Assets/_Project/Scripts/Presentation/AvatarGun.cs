using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>Etapas do revólver na mão da personagem (roleta e modo de brincadeira).</summary>
    public enum GunStage
    {
        None,
        /// <summary>O revólver vem a voar da máquina: o braço estica-se para o apanhar.</summary>
        Reach,
        /// <summary>Leva o revólver da frente do peito até à têmpora (t = 0..1).</summary>
        Raise,
        /// <summary>Cano encostado à têmpora, a tremer (t = tensão 0..1).</summary>
        Hold,
        /// <summary>Gatilho: o cão cai; t = 0..1 do coice.</summary>
        Fire,
        /// <summary>Sobreviveu: o braço desce devagar, alívio (t = 0..1).</summary>
        Relief,
        /// <summary>Modo de brincadeira: braço esticado a apontar.</summary>
        Aim
    }

    /// <summary>
    /// Revólver na mão: IK do braço direito para cada etapa e pose do revólver tirada da MÃO REAL depois do IK — o
    /// punho fica sempre dentro da luva e, na têmpora, a boca do cano encosta à superfície da cabeça (nunca a atravessa).
    /// </summary>
    public sealed partial class AvatarVisual
    {
        /// <summary>Escala do revólver na mão (o modelo tem ~26 cm; os braços curtos do boneco chegam a esta distância).</summary>
        public const float HeldGunScale = 1.1f;
        /// <summary>Boca do cano e centro da luva no espaço do revólver (escala 1, origem no punho, frente = +Z).</summary>
        public static readonly Vector3 GunMuzzleLocal = new Vector3(0f, 0.049f, 0.245f);
        /// <summary>Ponto do punho do revólver (espaço do revólver, escala 1) que passa pelo canal dos dedos.</summary>
        public static readonly Vector3 GunGloveLocal = new Vector3(0f, -0.045f, -0.016f);
        /// <summary>Escala da luva de punho fechado (Blender/gloves_export.py) para ficar do tamanho da luva aberta.</summary>
        public const float FistScale = 1.2f;
        /// <summary>Centro do canal dos dedos da luva de punho (espaço da mão, antes da escala).</summary>
        public static readonly Vector3 FistChannelLocal = new Vector3(-0.0071f, -0.1015f, 0f);
        /// <summary>
        /// Orientação do revólver dentro do punho: cano ao longo do antebraço (−Y da mão, a sair do pulso), punho da arma
        /// pelo canal dos dedos (+Z da mão, do indicador para o mindinho), nós dos dedos do lado de fora.
        /// </summary>
        public static readonly Quaternion GripInHand = Quaternion.LookRotation(Vector3.down, Vector3.forward);

        GunStage _gunStage;
        float _gunT;
        float _gunSetAt = -10f;
        Vector3 _gunAim = Vector3.forward;
        float _gunIk;
        float _gunRecoil;
        Vector3 _heldGunPos;
        Quaternion _heldGunRot = Quaternion.identity;
        Vector3 _reachTarget;

        /// <summary>Pedido por frame (RiskDeviceView / modo de brincadeira). Sem pedidos durante 0,2 s o braço volta ao normal.</summary>
        public void SetGun(GunStage stage, float t, Vector3 aimDirection = default, Vector3 reachTarget = default)
        {
            _gunStage = stage;
            _gunT = Mathf.Clamp01(t);
            _gunSetAt = Time.time;
            if (aimDirection.sqrMagnitude > 1e-6f) _gunAim = aimDirection.normalized;
            _reachTarget = reachTarget;
        }

        /// <summary>Coice visual (modo de brincadeira, cada disparo).</summary>
        public void GunKick() => _gunRecoil = 1f;

        bool GunRequested => _gunStage != GunStage.None && Time.time - _gunSetAt < 0.2f && !_ghost && !_popped && _knockAngle < 5f && !InRagdoll;

        /// <summary>Pose do revólver na mão (origem no punho). False se a personagem não o está a segurar.</summary>
        public bool TryGetHeldGun(out Vector3 position, out Quaternion rotation)
        {
            position = _heldGunPos;
            rotation = _heldGunRot;
            return _gunIk > 0.35f && _handR != null && GunRequested;
        }

        void ApplyGunIK(float dt, AvatarPose pose)
        {
            bool active = GunRequested;
            _gunIk = Damp(_gunIk, active ? 1f : 0f, active ? 9f : 10f, dt);
            _gunRecoil = Mathf.MoveTowards(_gunRecoil, 0f, dt * 5f);
            if (_gunIk < 0.01f || _shoulderR == null)
            {
                if (!_cueFistActive) SetFist(true, false);
                return;
            }

            Vector3 up = _rig.up, fwd = _rig.forward, right = _rig.right;
            Vector3 center = HeadCenter(out float radius);
            float s = HeldGunScale;
            float time = Time.time + _idleOffset;

            // Têmpora: ao lado da cabeça, um pouco à frente; o cano chega de fora, de baixo e da frente (é o ângulo a
            // que um braço curto chega — nas referências o cotovelo sobe, aqui o boneco inclina o revólver).
            Vector3 temple = center + right * (radius * 0.9f) + fwd * (radius * 0.3f) + up * (radius * 0.08f);
            // O cano chega à têmpora de fora, de baixo e da frente (o ângulo a que um braço curto chega).
            Vector3 outward = (right * 0.75f - up * 0.35f + fwd * 0.25f).normalized;
            var templeRot = Quaternion.LookRotation(-outward, Vector3.ProjectOnPlane(up, outward).normalized);
            Vector3 templeRoot = temple + outward * 0.006f - templeRot * (GunMuzzleLocal * s);

            // Peito: revólver à frente, a apontar para a frente e para baixo (apanhar / baixar).
            Vector3 chest = _shoulderR.position + fwd * 0.26f - up * 0.06f - right * 0.06f;
            var chestRot = Quaternion.LookRotation((fwd - up * 0.5f).normalized, up);
            Vector3 low = _shoulderR.position + fwd * 0.2f - up * 0.26f;
            var lowRot = Quaternion.LookRotation((fwd * 0.4f - up).normalized, fwd);

            Vector3 root;
            Quaternion rot;
            switch (_gunStage)
            {
                case GunStage.Reach:
                {
                    // Braço esticado para o revólver que chega (ou em frente, se ainda não se sabe de onde).
                    Vector3 want = _reachTarget.sqrMagnitude > 0f ? _reachTarget : _shoulderR.position + fwd * 0.34f + up * 0.12f;
                    Vector3 toWant = Vector3.ClampMagnitude(want - _shoulderR.position, 0.36f);
                    root = _shoulderR.position + toWant;
                    rot = Quaternion.LookRotation(toWant.sqrMagnitude > 1e-6f ? toWant.normalized : fwd, up);
                    break;
                }
                case GunStage.Raise:
                {
                    float e = Mathf.SmoothStep(0f, 1f, _gunT);
                    root = Vector3.Lerp(chest, templeRoot, e) + up * (Mathf.Sin(e * Mathf.PI) * 0.06f);
                    rot = Quaternion.Slerp(chestRot, templeRot, e);
                    break;
                }
                case GunStage.Hold:
                {
                    // Tremor que cresce com a tensão (o medo), sempre à volta do ponto de contacto.
                    float amp = Mathf.Lerp(0.0025f, 0.006f, _gunT);
                    Vector3 tremble = (up * Mathf.Sin(time * 31f) + fwd * Mathf.Sin(time * 23f + 1f) + right * Mathf.Sin(time * 17f + 2f) * 0.5f) * amp;
                    root = templeRoot + tremble;
                    rot = templeRot * Quaternion.Euler(Mathf.Sin(time * 27f) * 1.2f * _gunT, 0f, 0f);
                    break;
                }
                case GunStage.Fire:
                {
                    // Coice: a mão salta para fora e o cano levanta.
                    float kick = Mathf.Sin(Mathf.Clamp01(_gunT) * Mathf.PI) * (1f - _gunT * 0.5f);
                    root = templeRoot + outward * (0.05f * kick) + up * (0.03f * kick);
                    rot = templeRot * Quaternion.Euler(-25f * kick, 0f, 0f);
                    break;
                }
                case GunStage.Relief:
                {
                    float e = Mathf.SmoothStep(0f, 1f, _gunT);
                    root = Vector3.Lerp(templeRoot + outward * 0.03f, low, e);
                    rot = Quaternion.Slerp(templeRot, lowRot, e);
                    break;
                }
                default:
                {
                    // Aim: braço esticado na direcção do olhar (limitado ao que o ombro alcança).
                    Vector3 aim = _gunAim;
                    Vector3 grip = _shoulderR.position + aim * 0.33f - up * 0.02f - right * 0.04f;
                    grip -= aim * (0.05f * _gunRecoil);
                    root = grip - Quaternion.LookRotation(aim, up) * (GunGloveLocal * s);
                    rot = Quaternion.LookRotation(aim, up) * Quaternion.Euler(-35f * _gunRecoil, 0f, 0f);
                    break;
                }
            }

            // A mão fecha-se à volta do punho: o pulso roda para o punho da arma passar pelo canal dos dedos e o IK
            // leva o pivô da mão ao sítio que põe esse canal em cima do punho do revólver.
            var handRot = rot * Quaternion.Inverse(GripInHand);
            Vector3 gripWorld = root + rot * (GunGloveLocal * s);
            Vector3 handTarget = gripWorld - handRot * (FistChannelLocal * FistScale);
            // Cotovelo onde o antebraço continuaria a linha da mão (o pulso dobra o mínimo que os braços curtos deixam).
            Vector3 idealElbow = handTarget + handRot * Vector3.up * ForearmToGrip;
            Vector3 pole = idealElbow - _shoulderR.position;
            if (pole.sqrMagnitude < 1e-6f) pole = right - up * 0.3f;
            pole = (pole.normalized + (right * 0.6f - up * 0.3f) * 0.35f).normalized;
            SolveArm(_shoulderR, _elbowR, handTarget, pole, _gunIk);
            _handR.rotation = Quaternion.Slerp(_handR.rotation, handRot, _gunIk);
            SetFist(true, _gunIk > 0.4f);

            // O revólver segue a mão real (se o braço não chegar, o revólver continua no punho em vez de flutuar).
            Vector3 channel = _handR.TransformPoint(FistChannelLocal * FistScale);
            _heldGunRot = _handR.rotation * GripInHand;
            rot = _heldGunRot;
            _heldGunPos = channel - rot * (GunGloveLocal * s);
            if (_gunStage == GunStage.Hold || _gunStage == GunStage.Fire || _gunStage == GunStage.Raise)
            {
                // Nunca dentro da cabeça: se a boca do cano entrar na esfera, recua ao longo do cano.
                Vector3 muzzle = _heldGunPos + rot * (GunMuzzleLocal * s);
                float inside = radius * 0.97f - Vector3.Distance(muzzle, center);
                if (inside > 0f) _heldGunPos -= rot * Vector3.forward * inside;
            }
        }
    }
}
