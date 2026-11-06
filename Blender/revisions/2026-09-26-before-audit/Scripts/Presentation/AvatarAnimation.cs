using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Animação procedural das personagens, em camadas:
    ///  1. pose base do estado (parado, a mirar, na roleta, a festejar, fantasma), misturada suavemente;
    ///  2. locomoção: passo com o joelho a dobrar na fase aérea da perna (nunca com o pé apoiado), comprimento de
    ///     passada tirado da velocidade (sem pés a deslizar), ancas a rodar com a perna da frente e ombros em
    ///     contra-rotação, balanço lateral de brinquedo ("waddle"), inclinação ao acelerar e nas curvas;
    ///  3. corpo e olhar separados: o tronco vira-se para onde anda e só roda no lugar (com passinhos) quando a
    ///     câmara se afasta muito; a cabeça segue a câmara / o ponto de interesse;
    ///  4. física secundária: cabeça com mola (inércia), squash &amp; stretch nos saltos, aterragens e passos;
    ///  5. olhos que piscam, gestos ociosos, emotes e mãos no taco com IK de dois ossos (só se alcançam).
    /// Convenções (Unity): coxa X+ = perna para trás; joelho X+ = flexão; ombro X− = braço para a frente;
    /// tronco X+ = inclinar para a frente; Y+ = rodar para a direita; Z+ = topo para a esquerda.
    /// </summary>
    public sealed partial class AvatarVisual
    {
        const float UpperArm = 0.15f;
        const float ForearmToGrip = 0.23f;
        float _aimSpine = 55f;
        float _aimHead;
        bool _aiming;

        /// <summary>A mão direita agarra o taco a esta distância do fim do punho.</summary>
        public const float CueGripFromButt = 0.24f;

        struct Channels
        {
            public float HipsX, HipsY, HipsZ, HipsYaw, HipsRoll, SpineX, SpineY, SpineZ, SpineTwist, HeadX, HeadY, HeadZ;
            public float LegL, LegR, LegLZ, LegRZ, KneeL, KneeR, FootL, FootR;
            public Vector3 ShL, ShR;
            public float ElL, ElR;

            public static Channels Lerp(in Channels a, in Channels b, float t)
            {
                return new Channels
                {
                    HipsX = Mathf.LerpUnclamped(a.HipsX, b.HipsX, t),
                    HipsY = Mathf.LerpUnclamped(a.HipsY, b.HipsY, t),
                    HipsZ = Mathf.LerpUnclamped(a.HipsZ, b.HipsZ, t),
                    HipsYaw = Mathf.LerpUnclamped(a.HipsYaw, b.HipsYaw, t),
                    HipsRoll = Mathf.LerpUnclamped(a.HipsRoll, b.HipsRoll, t),
                    SpineX = Mathf.LerpUnclamped(a.SpineX, b.SpineX, t),
                    SpineY = Mathf.LerpUnclamped(a.SpineY, b.SpineY, t),
                    SpineZ = Mathf.LerpUnclamped(a.SpineZ, b.SpineZ, t),
                    SpineTwist = Mathf.LerpUnclamped(a.SpineTwist, b.SpineTwist, t),
                    HeadX = Mathf.LerpUnclamped(a.HeadX, b.HeadX, t),
                    HeadY = Mathf.LerpUnclamped(a.HeadY, b.HeadY, t),
                    HeadZ = Mathf.LerpUnclamped(a.HeadZ, b.HeadZ, t),
                    LegL = Mathf.LerpUnclamped(a.LegL, b.LegL, t),
                    LegR = Mathf.LerpUnclamped(a.LegR, b.LegR, t),
                    LegLZ = Mathf.LerpUnclamped(a.LegLZ, b.LegLZ, t),
                    LegRZ = Mathf.LerpUnclamped(a.LegRZ, b.LegRZ, t),
                    KneeL = Mathf.LerpUnclamped(a.KneeL, b.KneeL, t),
                    KneeR = Mathf.LerpUnclamped(a.KneeR, b.KneeR, t),
                    FootL = Mathf.LerpUnclamped(a.FootL, b.FootL, t),
                    FootR = Mathf.LerpUnclamped(a.FootR, b.FootR, t),
                    ShL = Vector3.LerpUnclamped(a.ShL, b.ShL, t),
                    ShR = Vector3.LerpUnclamped(a.ShR, b.ShR, t),
                    ElL = Mathf.LerpUnclamped(a.ElL, b.ElL, t),
                    ElR = Mathf.LerpUnclamped(a.ElR, b.ElR, t),
                };
            }
        }

        readonly List<Transform> _eyes = new List<Transform>();

        Channels _base;
        bool _baseInit;
        bool _motionInit;
        Vector3 _lastPosition;
        Vector3 _velocity;
        float _bodyYaw;
        float _bodyYawVel;
        bool _turningInPlace;
        float _bodyTurnRate;
        float _walkPhase;
        float _stride;
        float _run;
        float _backward;
        float _accelLean;
        float _prevSpeed;
        float _lastY;
        float _vy;
        float _air;
        float _squash;
        float _squashVel;
        float _emoteWeight;
        float _blinkTimer = 2f;
        float _blink = -1f;
        float _fidgetTimer = 6f;
        int _fidget = -1;
        float _fidgetTime;
        float _headYaw;
        float _headPitchExtra;
        float _headSpringPitch, _headSpringPitchVel, _headSpringRoll, _headSpringRollVel;
        Vector3 _prevNeck, _prevNeckVel, _neckAccel;
        bool _neckInit;
        float _knockAngle;
        float _knockVel;
        float _ikWeight;
        Vector3 _cueTip, _cueDir, _bridge;
        float _cueLength = 1.36f;
        float _ikSetTime = -10f;

        /// <summary>Um pé tocou no chão (para os passos soarem ao ritmo do andar).</summary>
        public event System.Action<Vector3> Footstep;

        /// <summary>Ponto que a cabeça segue (mesa, bola, quem está a jogar). Null = para onde o jogador olha.</summary>
        public Vector3? LookTarget { get; set; }

        /// <summary>Fase e peso do andar (a câmara em primeira pessoa usa-os para o balanço).</summary>
        public float WalkPhase => _walkPhase;
        public float WalkWeight => _stride;
        public float Squash => _squash;
        public Vector3 RightShoulderPosition => _shoulderR != null ? _shoulderR.position : transform.position;

        /// <summary>
        /// O taco deste frame (ponta, direcção para a bola e comprimento). As mãos agarram-no nos pontos mais
        /// próximos dos ombros: a esquerda em ponte mais à frente, a direita no punho.
        /// </summary>
        public void SetCue(Vector3 tip, Vector3 towardBall, float length, Vector3 bridge)
        {
            _bridge = bridge;
            _cueTip = tip;
            _cueDir = towardBall.normalized;
            _cueLength = length;
            _ikSetTime = Time.time;
        }

        static Quaternion E(Vector3 euler) => Quaternion.Euler(euler);

        static float Damp(float current, float target, float rate, float dt) => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        Channels BasePose(AvatarPose pose, float t, float pitch)
        {
            var c = new Channels
            {
                HipsY = HipHeight,
                SpineX = 2f,
                HeadX = pitch * 0.5f,
                KneeL = 4f,
                KneeR = 4f,
                LegL = -2f,
                LegR = -2f,
                LegLZ = -1.5f,
                LegRZ = 1.5f,
                ShL = new Vector3(5f, 0f, -9f),
                ShR = new Vector3(5f, 0f, 9f),
                ElL = -16f,
                ElR = -16f,
            };

            if (_ghost)
            {
                c.HipsY = HipHeight + 0.28f + Mathf.Sin(t * 1.6f) * 0.07f;
                c.LegL = 18f + Mathf.Sin(t * 1.7f) * 12f;
                c.LegR = 12f + Mathf.Sin(t * 1.9f + 1f) * 12f;
                c.KneeL = c.KneeR = 35f;
                c.FootL = c.FootR = 25f;
                c.ShL = new Vector3(-25f + Mathf.Sin(t * 1.3f) * 15f, 0f, -40f);
                c.ShR = new Vector3(-25f + Mathf.Sin(t * 1.3f + 2f) * 15f, 0f, 40f);
                c.ElL = c.ElR = -35f;
                c.SpineX = 8f + Mathf.Sin(t * 0.9f) * 4f;
                c.HeadZ = Mathf.Sin(t * 0.7f) * 10f;
                return c;
            }

            switch (pose)
            {
                case AvatarPose.Aiming:
                    // Tacada como nas referências: ancas para trás e baixas, tronco debruçado até o queixo ficar por
                    // cima do taco (_aimSpine sai da altura real do taco), torso rodado com o ombro direito atrás e a
                    // cabeça levantada a olhar ao longo do taco. Pés (FootHome) e braços (IK) assentam sozinhos.
                    // Como nas referências: o corpo fica à esquerda da linha do taco (ShotMath.StandBehind), ancas para
                    // trás, tronco quase deitado por cima do taco e rodado para a direita, cabeça por cima do taco a olhar
                    // para a bola. Braço esquerdo esticado com a mão apoiada na mesa (ponte); braço direito logo acima,
                    // alinhado com ele: cotovelo para cima e para trás, antebraço a cair e a mão no taco junto à anca.
                    // Valores tirados de um solver de colisões (o taco não pode tocar em tronco, bacia nem cabeça): corpo
                    // 28 cm à esquerda do taco, tronco dobrado (pela altura do taco) e rodado 38° à volta da própria
                    // coluna — ombro direito em cima e atrás, esquerdo em baixo e à frente — e inclinado para o taco.
                    // A cabeça fica direita a olhar para a bola (ApplyHead).
                    c.HipsX = 0f;
                    c.HipsZ = 0f;
                    c.HipsY = HipHeight - 0.01f;
                    c.HipsYaw = 15f;
                    c.SpineX = _aimSpine;
                    c.SpineY = 0f;
                    c.SpineZ = -14f;
                    c.SpineTwist = 38f;
                    c.HeadX = 0f;
                    c.LegL = -22f;
                    c.LegR = 18f;
                    c.KneeL = 26f;
                    c.KneeR = 6f;
                    c.ShL = new Vector3(-95f, 0f, 12f);
                    c.ElL = -12f;
                    c.ShR = new Vector3(35f, 0f, 20f);
                    c.ElR = -85f;
                    break;
                case AvatarPose.Risk:
                    // Revólver à cabeça, a tremer, joelhos a ceder de medo.
                    float tremble = Mathf.Sin(t * 31f) * 1.2f + Mathf.Sin(t * 17f) * 0.8f;
                    c.ShR = new Vector3(-148f, 0f, 20f);
                    c.ElR = -96f;
                    c.ShL = new Vector3(-38f, 0f, -26f);
                    c.ElL = -84f;
                    c.SpineX = -3f + tremble;
                    c.SpineZ = tremble * 0.6f;
                    c.KneeL = c.KneeR = 16f;
                    c.LegL = c.LegR = -8f;
                    c.HipsY = HipHeight - 0.03f;
                    c.HeadX = 6f;
                    break;
                case AvatarPose.Celebrate:
                    // Pulinhos com os braços no ar (o squash é aplicado ao aterrar).
                    float jump = Mathf.Abs(Mathf.Sin(t * 6.5f));
                    c.HipsY = HipHeight + jump * 0.18f;
                    c.ShL = new Vector3(-165f + Mathf.Sin(t * 9f) * 18f, 0f, -22f);
                    c.ShR = new Vector3(-165f - Mathf.Sin(t * 9f) * 18f, 0f, 22f);
                    c.ElL = c.ElR = -18f;
                    c.KneeL = c.KneeR = (1f - jump) * 38f;
                    c.LegL = c.LegR = -(1f - jump) * 18f;
                    c.FootL = c.FootR = jump * 25f;
                    c.SpineX = -8f;
                    c.HeadX = -12f;
                    break;
            }
            return c;
        }

        Channels EmotePose(Channels c, float t)
        {
            switch (_emote)
            {
                case 0: // gargalhada: dobra-se para trás, mãos na barriga, a sacudir
                    c.SpineX = -16f + Mathf.Sin(t * 22f) * 5f;
                    c.HeadX = -18f + Mathf.Sin(t * 22f) * 6f;
                    c.ShL = new Vector3(-30f, 0f, -24f);
                    c.ShR = new Vector3(-30f, 0f, 24f);
                    c.ElL = c.ElR = -105f;
                    c.HipsY += Mathf.Abs(Mathf.Sin(t * 11f)) * 0.02f;
                    break;
                case 1: // polegar para cima com aceno de cabeça
                    c.ShR = new Vector3(-80f, 0f, 12f);
                    c.ElR = -70f;
                    c.HeadX = Mathf.Sin(t * 8f) * 10f;
                    c.SpineZ = -4f;
                    break;
                case 2: // mãos na cabeça, desespero
                    c.ShL = new Vector3(-158f, 0f, -34f);
                    c.ShR = new Vector3(-158f, 0f, 34f);
                    c.ElL = c.ElR = -112f;
                    c.SpineZ = Mathf.Sin(t * 9f) * 7f;
                    c.SpineX = 12f;
                    c.KneeL = c.KneeR = 12f;
                    break;
                case 3: // punho no ar, vingativo
                    c.ShR = new Vector3(-150f + Mathf.Sin(t * 15f) * 14f, 0f, 10f);
                    c.ElR = -35f;
                    c.ShL = new Vector3(-20f, 0f, -18f);
                    c.ElL = -70f;
                    c.SpineX = -6f;
                    break;
            }
            return c;
        }

        /// <summary>Gestos quando está parado muito tempo: olhar à volta, espreguiçar, bater o pé, coçar a cabeça.</summary>
        Channels Fidget(Channels c, float weight)
        {
            if (_fidget < 0 || weight <= 0f) return c;
            var f = c;
            float u = _fidgetTime;
            switch (_fidget)
            {
                case 0:
                    f.HeadY = Mathf.Sin(u * 2.2f) * 45f;
                    break;
                case 1:
                    f.ShL = new Vector3(-170f, 0f, -12f);
                    f.ShR = new Vector3(-170f, 0f, 12f);
                    f.ElL = f.ElR = -30f;
                    f.SpineX = -10f;
                    f.HipsY += 0.02f;
                    break;
                case 2:
                    f.LegR = -10f;
                    f.KneeR = 10f + Mathf.Max(0f, Mathf.Sin(u * 14f)) * 18f;
                    f.FootR = -Mathf.Max(0f, Mathf.Sin(u * 14f)) * 15f;
                    f.SpineZ = -3f;
                    break;
                case 3:
                    f.ShR = new Vector3(-150f, 0f, 30f);
                    f.ElR = -120f + Mathf.Sin(u * 18f) * 10f;
                    f.HeadZ = -8f;
                    break;
            }
            return Channels.Lerp(c, f, weight);
        }

        /// <summary>Andar/correr, apontar o taco, segurar o revólver, celebrar, emotes, queda e fantasma.</summary>
        public void Animate(float dt, AvatarPose pose, float pitch, bool highlighted)
        {
            if (_rig == null || dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            float t = Time.time + _idleOffset;

            // ---------------------------------------------------------------- movimento observado
            // Funciona igual para o jogador local, remotos interpolados e bots: só olha para o transform.
            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPosition;
            _lastPosition = pos;
            bool teleport = !_motionInit || delta.sqrMagnitude > 1.5f * 1.5f;
            _motionInit = true;
            Vector3 raw = teleport ? Vector3.zero : delta / dt;
            _velocity = Vector3.Lerp(_velocity, new Vector3(raw.x, 0f, raw.z), 1f - Mathf.Exp(-dt * 10f));
            float speed = _velocity.magnitude;
            if (speed > 9f)
            {
                _velocity = Vector3.zero;
                speed = 0f;
            }

            // Saltos e quedas pela velocidade vertical.
            float vyRaw = teleport ? 0f : (pos.y - _lastY) / dt;
            _lastY = pos.y;
            float prevVy = _vy;
            _vy = Damp(_vy, vyRaw, 14f, dt);
            bool airborne = !_ghost && Mathf.Abs(_vy) > 0.9f;
            float prevAir = _air;
            _air = Damp(_air, airborne ? 1f : 0f, airborne ? 14f : 22f, dt);
            if (prevAir > 0.5f && _air <= 0.5f && prevVy < -1.5f) _squashVel -= Mathf.Clamp(-prevVy * 0.9f, 1f, 4f); // aterragem
            if (vyRaw > 2.5f && prevVy < 1f) _squashVel += 2.5f; // impulso do salto

            // ---------------------------------------------------------------- corpo vs. olhar
            float lookYaw = transform.eulerAngles.y;
            if (teleport) _bodyYaw = lookYaw;
            float desired = _bodyYaw;
            bool locked = pose == AvatarPose.Aiming || pose == AvatarPose.Risk || _ghost || _knockAngle > 5f;
            if (locked)
            {
                desired = lookYaw;
                _turningInPlace = false;
            }
            else if (speed > 0.35f)
            {
                // A andar: o tronco vira-se para onde vai (até 75° da câmara); para trás anda de costas.
                float moveYaw = Mathf.Atan2(_velocity.x, _velocity.z) * Mathf.Rad2Deg;
                float rel = Mathf.DeltaAngle(lookYaw, moveYaw);
                desired = Mathf.Abs(rel) > 115f ? lookYaw : lookYaw + Mathf.Clamp(rel, -75f, 75f);
                _turningInPlace = false;
            }
            else
            {
                // Parado: só roda quando a câmara se afasta muito, e aí roda com passinhos.
                float off = Mathf.DeltaAngle(_bodyYaw, lookYaw);
                if (Mathf.Abs(off) > 55f) _turningInPlace = true;
                if (_turningInPlace)
                {
                    desired = lookYaw;
                    if (Mathf.Abs(off) < 4f) _turningInPlace = false;
                }
            }
            float prevBodyYaw = _bodyYaw;
            _bodyYaw = Mathf.SmoothDampAngle(_bodyYaw, desired, ref _bodyYawVel, locked ? 0.08f : 0.14f, 600f, dt);
            float turnRate = Mathf.DeltaAngle(prevBodyYaw, _bodyYaw) / dt;
            _bodyTurnRate = Damp(_bodyTurnRate, turnRate, 10f, dt);

            var bodyRot = Quaternion.Euler(0f, _bodyYaw, 0f);
            Vector3 localVel = Quaternion.Inverse(bodyRot) * _velocity;
            _backward = Damp(_backward, speed > 0.3f && localVel.z < -0.35f * speed ? 1f : 0f, 8f, dt);
            float dir = _backward > 0.5f ? -1f : 1f;
            float accel = (speed - _prevSpeed) / dt;
            _prevSpeed = speed;
            _accelLean = Damp(_accelLean, Mathf.Clamp(accel * 2.2f, -8f, 12f), 5f, dt);

            // ---------------------------------------------------------------- pose base + emote + gestos
            _aiming = pose == AvatarPose.Aiming && !_ghost;
            if (pose == AvatarPose.Aiming && Time.time - _ikSetTime < 0.3f) UpdateAimSpine(dt);
            var target = BasePose(pose, t, pitch);
            bool idle = pose == AvatarPose.Idle && !_ghost && _stride < 0.1f && _emoteTimer <= 0f;
            UpdateFidget(dt, idle);
            float fidgetWeight = _fidget >= 0 ? Mathf.Sin(Mathf.Clamp01(_fidgetTime / 2.4f) * Mathf.PI) : 0f;
            target = Fidget(target, Mathf.Clamp01(fidgetWeight * 1.6f));

            if (_emoteTimer > 0f) _emoteTimer -= dt;
            _emoteWeight = Damp(_emoteWeight, _emoteTimer > 0f && !_ghost ? 1f : 0f, 10f, dt);
            if (_emoteWeight > 0.01f && _emote >= 0) target = Channels.Lerp(target, EmotePose(target, t), _emoteWeight);

            if (!_baseInit)
            {
                _base = target;
                _baseInit = true;
            }
            _base = Channels.Lerp(_base, target, 1f - Mathf.Exp(-dt * 10f));
            var c = _base;

            // Respiração (sempre, discreta) e peso a passar de uma perna para a outra.
            float breath = Mathf.Sin(t * 2.1f);
            float still = 1f - _stride;
            c.SpineX += breath * 1.2f * still;
            c.HipsY += breath * 0.004f * still;
            c.ShL.z -= breath * 1.5f;
            c.ShR.z += breath * 1.5f;
            if (pose == AvatarPose.Idle && !_ghost)
            {
                float sway = Mathf.Sin(t * 0.45f);
                c.HipsX += sway * 0.012f * still;
                c.HipsRoll -= sway * 2f * still;
                c.SpineZ += sway * 3f * still;
                c.HeadZ += Mathf.Sin(t * 0.6f + 1f) * 3f * still;
            }

            // ---------------------------------------------------------------- locomoção (pés com passos reais)
            bool legsIk = !_ghost && _air < 0.3f && _knockAngle < 5f && pose != AvatarPose.Celebrate;
            _ikLegs = Damp(_ikLegs, legsIk ? 1f : 0f, legsIk ? 12f : 25f, dt);
            _stride = Damp(_stride, _ghost ? 0f : Mathf.Clamp01(speed / 0.8f), 8f, dt);
            _run = Damp(_run, Mathf.Clamp01((speed - 2.2f) / 1.8f), 4f, dt);
            float w = _stride;
            UpdateFeet(dt, pose, speed, _bodyTurnRate);
            float cadence = Mathf.Clamp(2.8f + 1.9f * speed, 2.8f, 8.8f);
            if (w > 0.02f) _walkPhase = Mathf.Repeat(_walkPhase + dt * cadence * Mathf.PI, Mathf.PI * 2f);

            // O corpo acompanha os pés: bacia roda com o pé da frente, ombros ao contrário, braços em oposição,
            // bamboleio por cima da perna de apoio e ancas a subir e descer com o alcance das pernas.
            var invBody = Quaternion.Inverse(Quaternion.Euler(0f, _bodyYaw, 0f));
            Vector3 fl = invBody * (_footL.Ankle - transform.position);
            Vector3 fr = invBody * (_footR.Ankle - transform.position);
            float zl = (fl.z - FootHome(true, pose).y) * _ikLegs;
            float zr = (fr.z - FootHome(false, pose).y) * _ikLegs;
            float twist = Mathf.Clamp((zl - zr) * 36f, -13f, 13f);
            c.HipsYaw += twist;
            c.SpineY -= twist * 1.5f;
            float armAmp = Mathf.Lerp(30f, 52f, _run) * w;
            float al = Mathf.Clamp(zl / 0.17f, -1.3f, 1.3f), ar = Mathf.Clamp(zr / 0.17f, -1.3f, 1.3f);
            c.ShL.x += al * armAmp;
            c.ShR.x += ar * armAmp;
            c.ShL.z -= Mathf.Lerp(3f, 12f, _run) * w;
            c.ShR.z += Mathf.Lerp(3f, 12f, _run) * w;
            c.ElL -= (Mathf.Lerp(12f, 80f, _run) + Mathf.Max(0f, -al) * 25f) * w;
            c.ElR -= (Mathf.Lerp(12f, 80f, _run) + Mathf.Max(0f, -ar) * 25f) * w;
            float support = (_footL.Swinging ? 0f : 1f) - (_footR.Swinging ? 0f : 1f);
            _waddle = Damp(_waddle, support * w * _ikLegs, 12f, dt);
            c.HipsRoll += _waddle * 3.5f;
            c.SpineZ += _waddle * 2.5f;
            c.HipsX -= _waddle * 0.014f;
            c.SpineX += Mathf.Lerp(4f, 13f, _run) * w * (_backward > 0.5f ? -0.4f : 1f);
            float hipsTarget = c.HipsY - 0.012f * w + 0.02f * _run;
            if (_ikLegs > 0.01f) hipsTarget = Mathf.Min(hipsTarget, LegHeightBound(c.HipsX, c.HipsZ, c.HipsYaw) - transform.position.y);
            _hipsIk = Mathf.SmoothDamp(_hipsIk, hipsTarget, ref _hipsIkVel, 0.045f, 3f, dt);
            c.HipsY = Mathf.Lerp(c.HipsY, _hipsIk, _ikLegs);

            // Inclinação: para a frente ao acelerar, para dentro das curvas.
            c.SpineX += _accelLean * (1f - _air);
            float bank = Mathf.Clamp(-_bodyTurnRate * 0.035f * Mathf.Clamp01(speed / 1.5f), -12f, 12f);
            c.SpineZ += bank;
            c.HipsRoll += bank * 0.4f;
            if (_ghost)
            {
                c.SpineX += Mathf.Clamp(localVel.z * 5f, -12f, 12f);
                c.SpineZ -= Mathf.Clamp(localVel.x * 5f, -12f, 12f);
            }

            // No ar: pernas encolhidas e braços a equilibrar (sobe com uma perna à frente, desce a preparar).
            if (_air > 0.01f)
            {
                var airPose = c;
                bool up = _vy > 0f;
                airPose.LegL = up ? -34f : -14f;
                airPose.LegR = up ? 12f : -24f;
                airPose.KneeL = up ? 60f : 30f;
                airPose.KneeR = up ? 28f : 42f;
                airPose.FootL = airPose.FootR = 15f;
                airPose.ShL = new Vector3(-55f, 0f, -40f);
                airPose.ShR = new Vector3(-55f, 0f, 40f);
                airPose.ElL = airPose.ElR = -30f;
                airPose.SpineX = up ? -4f : 8f;
                c = Channels.Lerp(c, airPose, _air);
            }

            // A cabeça compensa parte da inclinação do tronco (olhos no horizonte).
            c.HeadX -= (c.SpineX - _base.SpineX) * 0.5f;
            c.HeadZ -= (c.SpineZ + c.HipsRoll) * 0.6f;

            // ---------------------------------------------------------------- aplicar
            _hips.localPosition = new Vector3(c.HipsX, c.HipsY, c.HipsZ);
            _hips.localRotation = Quaternion.Euler(0f, c.HipsYaw, c.HipsRoll);
            _spine.localRotation = Quaternion.Euler(c.SpineX, c.SpineY - c.HipsYaw, c.SpineZ - c.HipsRoll) * Quaternion.Euler(0f, c.SpineTwist, 0f);

            _thighL.localRotation = Quaternion.Euler(c.LegL, 0f, c.LegLZ - c.HipsRoll);
            _thighR.localRotation = Quaternion.Euler(c.LegR, 0f, c.LegRZ - c.HipsRoll);
            _kneeL.localRotation = Quaternion.Euler(c.KneeL, 0f, 0f);
            _kneeR.localRotation = Quaternion.Euler(c.KneeR, 0f, 0f);
            // Pé plano no chão: anula coxa + joelho e aplica só a inclinação pedida para o pé.
            _ankleL.localRotation = Quaternion.Euler(-(c.LegL + c.KneeL) + c.FootL, 0f, -(c.LegLZ - c.HipsRoll) * 0.9f);
            _ankleR.localRotation = Quaternion.Euler(-(c.LegR + c.KneeR) + c.FootR, 0f, -(c.LegRZ - c.HipsRoll) * 0.9f);
            if (_ikLegs > 0.001f)
            {
                SolveLeg(_thighL, _kneeL, _ankleL, _footL, _ikLegs);
                SolveLeg(_thighR, _kneeR, _ankleR, _footR, _ikLegs);
            }
            _shoulderL.localRotation = E(c.ShL);
            _shoulderR.localRotation = E(c.ShR);
            _elbowL.localRotation = Quaternion.Euler(c.ElL, 0f, 0f);
            _elbowR.localRotation = Quaternion.Euler(c.ElR, 0f, 0f);

            ApplyKnockdown(dt, lookYaw);
            ApplyHead(c, dt, lookYaw);
            ApplyCueIK(dt, pose);
            UpdateEyes(dt, pose);
            ApplyRigScale(dt, highlighted);
            UpdateStars(t);
        }

        /// <summary>
        /// Inclinação do tronco na tacada: o pescoço fica ~6 cm acima do taco no sítio onde o taco passa por baixo
        /// do queixo (o taco sobe para trás, por isso quanto mais alto o taco, mais direito fica o boneco).
        /// </summary>
        void UpdateAimSpine(float dt)
        {
            Vector3 back = -_cueDir;
            var bh = new Vector2(back.x, back.z);
            float bl = bh.magnitude;
            if (bl < 1e-4f) return;
            bh /= bl;
            float slope = back.y / bl;
            Vector3 body = transform.position;
            float sBody = Vector2.Dot(new Vector2(body.x - _cueTip.x, body.z - _cueTip.z), bh);
            float neckForward = 0.417f * Mathf.Sin(_aimSpine * Mathf.Deg2Rad);
            float cueAtNeck = _cueTip.y + (sBody - neckForward) * slope - body.y;
            // Queixo ~16 cm acima do taco; entre 45° e 76° (o solver mostra que fora disto o taco toca no corpo).
            float cos = (cueAtNeck + 0.16f - (_hipsIk + 0.04f)) / 0.417f;
            float target = Mathf.Clamp(Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg, 45f, 76f);
            _aimSpine = Damp(_aimSpine, target, 6f, dt);
        }

        void UpdateFidget(float dt, bool idle)
        {
            if (!idle)
            {
                if (_fidget >= 0 && _fidgetTime > 0.3f) _fidgetTime = Mathf.Max(_fidgetTime, 2.1f);
                _fidgetTimer = Mathf.Max(_fidgetTimer, 3f);
            }
            if (_fidget >= 0)
            {
                _fidgetTime += dt;
                if (_fidgetTime >= 2.4f) _fidget = -1;
                return;
            }
            if (!idle) return;
            _fidgetTimer -= dt;
            if (_fidgetTimer > 0f) return;
            _fidget = Random.Range(0, 4);
            _fidgetTime = 0f;
            _fidgetTimer = Random.Range(6f, 13f);
        }

        /// <summary>
        /// Cabeça: olha para onde o jogador olha (ou para o ponto de interesse), com limites de pescoço, e tem uma
        /// mola de inércia (acenos ao arrancar, travar e pousar os pés) — a cabeçorra não é um bloco rígido.
        /// </summary>
        void ApplyHead(Channels c, float dt, float lookYaw)
        {
            float worldYaw = lookYaw;
            float pitchTarget = 0f;
            if (LookTarget.HasValue && !_ghost)
            {
                Vector3 to = LookTarget.Value - _head.position;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                if (flat.sqrMagnitude > 0.01f)
                {
                    worldYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                    pitchTarget = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg * 0.5f, -20f, 20f);
                }
            }
            // Relativo ao tronco (que já tem o corpo, as ancas e a contra-rotação dos ombros).
            float rel = Mathf.DeltaAngle(_bodyYaw + c.SpineY, worldYaw) + c.HeadY;
            _headYaw = Damp(_headYaw, Mathf.Clamp(rel, -75f, 75f), 8f, dt);
            _headPitchExtra = Damp(_headPitchExtra, pitchTarget, 6f, dt);

            // Mola de inércia a partir da aceleração do pescoço.
            Vector3 neck = _neck.position;
            if (!_neckInit)
            {
                _prevNeck = neck;
                _prevNeckVel = Vector3.zero;
                _neckInit = true;
            }
            Vector3 vel = (neck - _prevNeck) / dt;
            if (vel.sqrMagnitude > 100f) vel = _prevNeckVel;
            Vector3 acc = (vel - _prevNeckVel) / dt;
            _prevNeck = neck;
            _prevNeckVel = vel;
            _neckAccel = Vector3.Lerp(_neckAccel, Vector3.ClampMagnitude(acc, 40f), 1f - Mathf.Exp(-dt * 14f));
            Vector3 la = Quaternion.Inverse(_rig.rotation) * _neckAccel;
            float springPitchTarget = Mathf.Clamp(-la.z * 0.7f + la.y * 0.3f, -11f, 11f);
            float springRollTarget = Mathf.Clamp(la.x * 0.7f, -9f, 9f);
            const float k = 150f, damping = 9f;
            _headSpringPitchVel += ((springPitchTarget - _headSpringPitch) * k - _headSpringPitchVel * damping) * dt;
            _headSpringPitch += _headSpringPitchVel * dt;
            _headSpringRollVel += ((springRollTarget - _headSpringRoll) * k - _headSpringRollVel * damping) * dt;
            _headSpringRoll += _headSpringRollVel * dt;

            var normalLocal = Quaternion.Euler(Mathf.Clamp(c.HeadX + _headPitchExtra + _headSpringPitch, -60f, 45f), _headYaw,
                (_ghost ? c.HeadZ + 8f : c.HeadZ) + _headSpringRoll);
            _aimHead = Damp(_aimHead, _aiming ? 1f : 0f, 8f, dt);
            if (_aimHead > 0.01f)
            {
                // Na tacada o tronco está dobrado e torcido: a cabeça fica direita, virada para a bola, a olhar um
                // pouco para baixo (como nas fotos), em vez de herdar a torção (efeito "exorcista").
                float pitchW = 14f;
                if (LookTarget.HasValue)
                {
                    Vector3 to = LookTarget.Value - _head.position;
                    pitchW = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg, 0f, 30f);
                }
                var world = Quaternion.Euler(pitchW + _headSpringPitch * 0.5f, worldYaw, _headSpringRoll * 0.5f);
                var aimLocal = Quaternion.Inverse(_neck.rotation) * world;
                _head.localRotation = Quaternion.Slerp(normalLocal, aimLocal, _aimHead);
            }
            else
            {
                _head.localRotation = normalLocal;
            }
        }

        /// <summary>IK analítico de dois ossos: ombro → cotovelo → mão, com o cotovelo virado para <paramref name="pole"/>.</summary>
        void SolveArm(Transform shoulder, Transform elbow, Vector3 target, Vector3 pole, float weight)
        {
            Vector3 s = shoulder.position;
            Vector3 toTarget = target - s;
            float d = Mathf.Clamp(toTarget.magnitude, 0.05f, UpperArm + ForearmToGrip - 0.002f);
            Vector3 dir = toTarget.normalized;
            Vector3 n = Vector3.Cross(dir, pole);
            if (n.sqrMagnitude < 1e-5f) n = _rig.right;
            n.Normalize();
            Vector3 perp = Vector3.Cross(n, dir).normalized;
            float cosA = Mathf.Clamp((UpperArm * UpperArm + d * d - ForearmToGrip * ForearmToGrip) / (2f * UpperArm * d), -1f, 1f);
            float a = Mathf.Acos(cosA);
            Vector3 elbowPos = s + (dir * Mathf.Cos(a) + perp * Mathf.Sin(a)) * UpperArm;
            Vector3 hand = s + dir * d;
            Vector3 u = (elbowPos - s).normalized;
            Vector3 f = (hand - elbowPos).normalized;
            Vector3 z = f - u * Vector3.Dot(f, u);
            if (z.sqrMagnitude < 1e-6f) z = _rig.forward;
            var shoulderRot = Quaternion.LookRotation(z.normalized, -u);
            float bend = Vector3.Angle(u, f);
            shoulder.rotation = Quaternion.Slerp(shoulder.rotation, shoulderRot, weight);
            elbow.localRotation = Quaternion.Slerp(elbow.localRotation, Quaternion.Euler(-bend, 0f, 0f), weight);
        }

        /// <summary>Mãos no taco: pontos do taco mais próximos de cada ombro; só se o taco estiver ao alcance.</summary>
        void ApplyCueIK(float dt, AvatarPose pose)
        {
            bool recent = !_ghost && Time.time - _ikSetTime < 0.25f;
            Vector3 left = Vector3.zero, right = Vector3.zero;
            float reachable = 0f;
            if (recent)
            {
                float reach = UpperArm + ForearmToGrip;
                // Parâmetro ao longo do taco: 0 na ponta, cresce para o punho.
                // Direita: punho logo atrás do ombro (cotovelo para cima, antebraço a cair). Esquerda: ponte no pano
                // pouco atrás da branca; se a bola está longe, a ponte recua ao longo do taco até onde o braço chega.
                // O punho está num ponto fixo do taco (a mão acompanha o taco quando puxa e dá a tacada).
                float gripT = Mathf.Max(0.3f, _cueLength - CueGripFromButt);
                right = _cueTip - _cueDir * gripT;
                float bridgeT = Mathf.Max(0.1f, Vector3.Dot(_bridge - _cueTip, -_cueDir));
                Vector3 sl = _shoulderL.position;
                float tc = Vector3.Dot(sl - _cueTip, -_cueDir);
                float perp = Vector3.Distance(sl, _cueTip - _cueDir * tc);
                float comfy = reach * 0.93f;
                if (perp < comfy) bridgeT = Mathf.Max(bridgeT, tc - Mathf.Sqrt(comfy * comfy - perp * perp));
                bridgeT = Mathf.Min(bridgeT, gripT - 0.3f);
                left = _cueTip - _cueDir * bridgeT;
                left.y = Mathf.Max(left.y - 0.022f, _bridge.y);
                float worst = Mathf.Max(Vector3.Distance(_shoulderL.position, left), Vector3.Distance(_shoulderR.position, right));
                reachable = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(reach * 1.05f, reach * 1.5f, worst));
            }
            _ikWeight = Damp(_ikWeight, recent ? reachable : 0f, recent ? 10f : 6f, dt);
            if (_ikWeight < 0.01f) return;
            // Cotovelo direito para cima e para trás (braço de tacada); esquerdo para fora e ligeiramente para baixo.
            // Cotovelo direito para cima e para trás (antebraço a cair até ao taco, como nas fotos de referência).
            Vector3 poleR = (_rig.up * 0.8f - _rig.forward * 0.55f + _rig.right * 0.25f).normalized;
            Vector3 poleL = (-_rig.right * 0.85f - _rig.up * 0.3f + _rig.forward * 0.2f).normalized;
            SolveArm(_shoulderL, _elbowL, left, poleL, _ikWeight);
            SolveArm(_shoulderR, _elbowR, right, poleR, _ikWeight);
        }

        void UpdateEyes(float dt, AvatarPose pose)
        {
            if (_eyes.Count == 0) return;
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f && _blink < 0f)
            {
                _blink = 0f;
                _blinkTimer = Random.Range(2f, 5.5f);
                if (Random.value < 0.15f) _blinkTimer = 0.25f; // piscadela dupla de vez em quando
            }
            float open = 1f;
            if (_blink >= 0f)
            {
                _blink += dt;
                float k = _blink / 0.14f;
                open = 1f - Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * 0.92f;
                if (k >= 1f) _blink = -1f;
            }
            float widthScale = 1f, heightScale = open;
            if (_ghost) heightScale *= 0.55f;
            else if (pose == AvatarPose.Celebrate || (_emoteTimer > 0f && _emote == 0)) heightScale *= 0.45f; // olhos felizes
            else if (pose == AvatarPose.Risk)
            {
                widthScale = 1.18f;
                heightScale *= 1.12f + Mathf.Sin(Time.time * 40f) * 0.03f;
            }
            else if (_knockAngle > 20f) heightScale *= 0.3f;
            foreach (var e in _eyes)
            {
                if (e == null) continue;
                var target = new Vector3(widthScale, heightScale, 1f);
                e.localScale = Vector3.Lerp(e.localScale, target, 1f - Mathf.Exp(-dt * 30f));
            }
        }

        void ApplyRigScale(float dt, bool highlighted)
        {
            // Mola do squash & stretch (aterragens, saltos, passos, pop).
            const float k = 260f, damping = 16f;
            _squashVel += (-k * _squash - damping * _squashVel) * dt;
            _squash += _squashVel * dt;
            _squash = Mathf.Clamp(_squash, -0.3f, 0.3f);

            float scale = highlighted ? 1.08f : 1f;
            if (_popTimer >= 0f)
            {
                _popTimer += dt;
                float pop = _popTimer < 0.12f ? 1f + _popTimer * 3f : Mathf.Max(0f, 1.36f - (_popTimer - 0.12f) * 9f);
                scale *= pop;
                if (_popTimer > 0.3f)
                {
                    _popTimer = -1f;
                    _popped = true;
                }
            }
            if (_popped && !_ghost) scale = 0.001f;
            var squash = new Vector3(1f - _squash * 0.5f, 1f + _squash, 1f - _squash * 0.5f);
            var targetScale = squash * scale;
            _rig.localScale = _popTimer >= 0f ? targetScale : Vector3.Lerp(_rig.localScale, targetScale, 1f - Mathf.Exp(-dt * 20f));
        }

        void ApplyKnockdown(float dt, float lookYaw)
        {
            // Queda para trás com mola (cai depressa, levanta com um ressalto).
            float target = _knockdown > 0f ? 82f : 0f;
            if (_knockdown > 0f)
            {
                _knockdown -= dt;
                if (_knockdown <= 0f) _squashVel += 1.8f;
            }
            float stiffness = _knockdown > 0f ? 120f : 60f;
            _knockVel += ((target - _knockAngle) * stiffness - _knockVel * 12f) * dt;
            _knockAngle += _knockVel * dt;
            if (Mathf.Abs(_knockAngle) < 0.01f && Mathf.Abs(_knockVel) < 0.01f) _knockAngle = 0f;
            // O rig roda em relação ao pai: diferença entre o corpo e o olhar + queda.
            float yawOffset = Mathf.DeltaAngle(lookYaw, _bodyYaw);
            _rig.localRotation = Quaternion.Euler(0f, yawOffset, 0f) * Quaternion.Euler(-Mathf.Clamp(_knockAngle, -10f, 95f), 0f, 0f);
        }

        void UpdateStars(float t)
        {
            bool dizzy = Debuffed && !_ghost;
            for (int i = 0; i < _stars.Length; i++)
            {
                if (_stars[i] == null) continue;
                _stars[i].gameObject.SetActive(dizzy);
                if (!dizzy) continue;
                float a = t * 3f + i * Mathf.PI * 2f / _stars.Length;
                _stars[i].localPosition = new Vector3(Mathf.Cos(a) * 0.3f, Mathf.Sin(a * 2f) * 0.03f, Mathf.Sin(a) * 0.3f);
                _stars[i].localRotation = Quaternion.Euler(0f, t * 200f, 45f);
            }
        }
    }
}
