using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Pernas com passos de verdade: cada pé fica preso ao chão (em coordenadas do mundo) enquanto apoia e só se
    /// move num passo — levanta, avança num arco e pousa onde o corpo vai estar. Os ângulos da coxa e do joelho
    /// saem de IK analítico (dois ossos), por isso os pés nunca flutuam nem deslizam; a altura das ancas desce
    /// quando as pernas precisam de alcançar (é isso que dá o balanço do andar). Serve para andar, correr, andar
    /// para trás ou de lado, rodar no lugar (passinhos), arrancar e parar (passos de acerto) e para assentar os
    /// pés nas posturas (tacada, roleta).
    /// </summary>
    public sealed partial class AvatarVisual
    {
        const float ThighLen = 0.19f;
        const float ShinLen = 0.17f;
        /// <summary>Alcance útil da perna (joelho sempre um pouco flectido).</summary>
        const float LegReach = 0.356f;
        /// <summary>Altura do tornozelo com a sola no chão.</summary>
        const float AnkleGround = 0.103f;
        /// <summary>Distância vertical anca → articulação da coxa.</summary>
        const float PivotDrop = 0.05f;
        const float PivotX = 0.078f;

        sealed class Foot
        {
            public bool Left;
            public Vector3 Planted;
            public float PlantedYaw;
            public bool Swinging;
            public Vector3 From, To;
            public float FromYaw, ToYaw;
            public float T, Dur, Lift;
            // Resultado deste frame (mundo).
            public Vector3 Ankle;
            public float Yaw;
            public float Pitch;
        }

        readonly Foot _footL = new Foot { Left = true };
        readonly Foot _footR = new Foot { Left = false };
        bool _feetInit;
        float _ikLegs;
        float _stepTimer = 10f;
        bool _nextLeft = true;
        float _hipsIk = HipHeight;
        float _hipsIkVel;
        float _waddle;
        float _tap;

        /// <summary>Onde cada pé gosta de estar, em relação ao corpo (x = direita, y = frente).</summary>
        static Vector2 FootHome(bool left, AvatarPose pose)
        {
            switch (pose)
            {
                case AvatarPose.Aiming: return left ? new Vector2(-0.13f, 0.09f) : new Vector2(0.09f, -0.11f);
                case AvatarPose.Risk: return left ? new Vector2(-0.12f, 0.03f) : new Vector2(0.12f, -0.01f);
                default: return left ? new Vector2(-0.085f, 0.015f) : new Vector2(0.085f, -0.015f);
            }
        }

        Vector3 HomeWorld(bool left, AvatarPose pose, float yaw)
        {
            var h = FootHome(left, pose);
            return transform.position + Quaternion.Euler(0f, yaw, 0f) * new Vector3(h.x, 0f, h.y);
        }

        void PlantHere(Foot f, AvatarPose pose)
        {
            // Assenta o pé por baixo de onde está agora (ou na posição de casa, se ainda não há esqueleto).
            var ankle = f.Left ? _ankleL : _ankleR;
            Vector3 p = ankle != null && _feetInit ? ankle.position : HomeWorld(f.Left, pose, _bodyYaw);
            p.y = transform.position.y;
            f.Planted = p;
            f.PlantedYaw = _bodyYaw + (f.Left ? -5f : 5f);
            f.Swinging = false;
        }

        /// <summary>Onde o pé deve pousar: debaixo da anca no fim do passo, um pouco à frente (meio apoio).</summary>
        Vector3 Landing(Foot f, AvatarPose pose, float swingDur, float stanceDur, float yawRate, out float yaw)
        {
            float remaining = f.Swinging ? Mathf.Max(0f, (1f - f.T) * f.Dur) : swingDur;
            yaw = _bodyYaw + Mathf.Clamp(yawRate * remaining, -35f, 35f);
            Vector3 body = transform.position + _velocity * remaining;
            var h = FootHome(f.Left, pose);
            Vector3 home = body + Quaternion.Euler(0f, yaw, 0f) * new Vector3(h.x, 0f, h.y);
            Vector3 lead = Vector3.ClampMagnitude(_velocity * (stanceDur * 0.5f), 0.19f);
            yaw += f.Left ? -5f : 5f;
            Vector3 p = home + lead;
            p.y = transform.position.y;
            return p;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void UpdateFeet(float dt, AvatarPose pose, float speed, float yawRate)
        {
            if (!_feetInit || _ikLegs < 0.02f)
            {
                PlantHere(_footL, pose);
                PlantHere(_footR, pose);
                _feetInit = true;
            }
            // Pés perdidos (teleporte, lag de rede): recomeça.
            if (Flat(_footL.Planted, HomeWorld(true, pose, _bodyYaw)) > 0.9f || Flat(_footR.Planted, HomeWorld(false, pose, _bodyYaw)) > 0.9f)
            {
                _footL.Swinging = _footR.Swinging = false;
                _footL.Planted = HomeWorld(true, pose, _bodyYaw);
                _footR.Planted = HomeWorld(false, pose, _bodyYaw);
                _footL.PlantedYaw = _footR.PlantedYaw = _bodyYaw;
            }

            // Cadência (passos por segundo) e fracção de apoio: pernas curtas de brinquedo → passos rápidos.
            float cadence = Mathf.Clamp(2.8f + 1.9f * speed, 2.8f, Mathf.Lerp(7.5f, 8.8f, _run));
            float duty = Mathf.Lerp(0.6f, 0.38f, _run);
            float period = 1f / cadence;
            float swingDur = Mathf.Clamp((1f - duty) * 2f / cadence, 0.1f, 0.22f);
            float stanceDur = duty * 2f / cadence;
            float lift = Mathf.Clamp(0.04f + 0.03f * speed, 0.04f, 0.11f);
            _stepTimer += dt;

            foreach (var f in new[] { _footL, _footR })
            {
                if (!f.Swinging) continue;
                f.T += dt / f.Dur;
                // Reajusta o alvo com suavidade (mudanças de velocidade e de direcção a meio do passo).
                var target = Landing(f, pose, swingDur, stanceDur, yawRate, out float targetYaw);
                float k = 1f - Mathf.Exp(-dt * 14f);
                f.To = Vector3.Lerp(f.To, target, k);
                f.ToYaw = Mathf.LerpAngle(f.ToYaw, targetYaw, k);
                if (f.T >= 1f)
                {
                    f.Swinging = false;
                    f.Planted = f.To;
                    f.PlantedYaw = f.ToYaw;
                    Footstep?.Invoke(f.Planted);
                    _squashVel -= Mathf.Lerp(0.18f, 0.5f, _run) * Mathf.Clamp01(speed / 1.5f);
                }
            }

            var next = _nextLeft ? _footL : _footR;
            var other = _nextLeft ? _footR : _footL;
            if (!next.Swinging && _stepTimer >= period)
            {
                bool otherOk = !other.Swinging || (_run > 0.4f && other.T > 0.5f);
                var target = Landing(next, pose, swingDur, stanceDur, yawRate, out float targetYaw);
                bool need = NeedsStep(next, target, targetYaw, speed);
                if (otherOk && need)
                {
                    next.Swinging = true;
                    next.T = 0f;
                    next.Dur = swingDur;
                    next.From = next.Planted;
                    next.FromYaw = next.PlantedYaw;
                    next.To = target;
                    next.ToYaw = targetYaw;
                    // Passos de acerto (parado) levantam pouco; a andar/correr levantam bem o pé.
                    next.Lift = Mathf.Lerp(0.03f, lift, Mathf.Clamp01(Flat(next.From, target) / 0.2f + speed * 0.5f));
                    _stepTimer = 0f;
                    _nextLeft = !_nextLeft;
                }
                else if (!need && !other.Swinging)
                {
                    var otherTarget = Landing(other, pose, swingDur, stanceDur, yawRate, out float otherYaw);
                    if (NeedsStep(other, otherTarget, otherYaw, speed)) _nextLeft = !_nextLeft;
                }
            }

            // Posições deste frame.
            float ground = transform.position.y;
            _tap = _fidget == 2 ? Mathf.Max(0f, Mathf.Sin(_fidgetTime * 14f)) * 0.035f * Mathf.Sin(Mathf.Clamp01(_fidgetTime / 2.4f) * Mathf.PI) : 0f;
            foreach (var f in new[] { _footL, _footR })
            {
                if (f.Swinging)
                {
                    float u = Mathf.Clamp01(f.T);
                    float e = u * u * (3f - 2f * u);
                    Vector3 p = Vector3.Lerp(f.From, f.To, e);
                    p.y = ground + AnkleGround + f.Lift * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.85f));
                    f.Ankle = p;
                    f.Yaw = Mathf.LerpAngle(f.FromYaw, f.ToYaw, e);
                    // Ponta para baixo ao levantar, calcanhar primeiro ao pousar.
                    float pitch = 26f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.4f)) * (1f - u) - 14f * u * u * u;
                    f.Pitch = Mathf.Lerp(f.Pitch, pitch, 1f - Mathf.Exp(-dt * 30f));
                }
                else
                {
                    f.Ankle = new Vector3(f.Planted.x, ground + AnkleGround + (f.Left ? 0f : _tap), f.Planted.z);
                    f.Yaw = f.PlantedYaw;
                    f.Pitch = Mathf.Lerp(f.Pitch, f.Left ? 0f : -_tap * 500f, 1f - Mathf.Exp(-dt * 20f));
                }
            }
        }

        bool NeedsStep(Foot f, Vector3 target, float targetYaw, float speed)
        {
            float d = Flat(f.Planted, target);
            float ang = Mathf.Abs(Mathf.DeltaAngle(f.PlantedYaw, targetYaw));
            return d > (speed > 0.15f ? 0.025f : 0.05f) || ang > 24f;
        }

        /// <summary>Altura máxima das ancas para as pernas apoiadas chegarem ao chão (é daqui que vem o balanço).</summary>
        float LegHeightBound(float hipsX, float hipsZ, float hipsYaw)
        {
            float bound = float.MaxValue;
            var body = Quaternion.Inverse(Quaternion.Euler(0f, _bodyYaw, 0f));
            var yawRot = Quaternion.Euler(0f, hipsYaw, 0f);
            foreach (var f in new[] { _footL, _footR })
            {
                if (f.Swinging) continue;
                Vector3 local = body * (f.Ankle - transform.position);
                Vector3 pivot = new Vector3(hipsX, 0f, hipsZ) + yawRot * new Vector3(f.Left ? -PivotX : PivotX, 0f, 0f);
                float dx = local.x - pivot.x, dz = local.z - pivot.z;
                float h2 = dx * dx + dz * dz;
                float up = Mathf.Sqrt(Mathf.Max(0.0025f, LegReach * LegReach - h2));
                bound = Mathf.Min(bound, local.y + up + PivotDrop);
            }
            return bound;
        }

        /// <summary>IK analítico da perna (coxa com Euler(x, 0, z), joelho em X) e pé com a orientação pedida.</summary>
        void SolveLeg(Transform thigh, Transform knee, Transform ankle, Foot f, float weight)
        {
            Vector3 t = _hips.InverseTransformPoint(f.Ankle) - thigh.localPosition;
            float d = t.magnitude;
            float maxD = ThighLen + ShinLen - 0.0005f;
            if (d > maxD)
            {
                t *= maxD / d;
                d = maxD;
            }
            else if (d < 0.1f)
            {
                t = (d > 1e-4f ? t / d : Vector3.down) * 0.1f;
                d = 0.1f;
            }
            float cosK = (d * d - ThighLen * ThighLen - ShinLen * ShinLen) / (2f * ThighLen * ShinLen);
            float k = Mathf.Acos(Mathf.Clamp(cosK, -1f, 1f));
            float a = -(ThighLen + ShinLen * Mathf.Cos(k));
            float b = -ShinLen * Mathf.Sin(k);
            float z = Mathf.Asin(Mathf.Clamp(t.x / -a, -1f, 1f));
            float x = Mathf.Atan2(t.z, t.y) - Mathf.Atan2(b, a * Mathf.Cos(z));
            var thighRot = Quaternion.Euler(x * Mathf.Rad2Deg, 0f, z * Mathf.Rad2Deg);
            var kneeRot = Quaternion.Euler(k * Mathf.Rad2Deg, 0f, 0f);
            thigh.localRotation = Quaternion.Slerp(thigh.localRotation, thighRot, weight);
            knee.localRotation = Quaternion.Slerp(knee.localRotation, kneeRot, weight);
            var want = Quaternion.Euler(0f, f.Yaw, 0f) * Quaternion.Euler(f.Pitch, 0f, 0f);
            ankle.localRotation = Quaternion.Slerp(ankle.localRotation, Quaternion.Inverse(knee.rotation) * want, weight);
        }
    }
}
