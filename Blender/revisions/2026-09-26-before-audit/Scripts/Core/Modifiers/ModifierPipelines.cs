using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Core.Modifiers
{
    /// <summary>Parâmetros de tacada antes/depois do pipeline (BaseShotParameters → ValidatedEffectiveShot).</summary>
    public struct ShotParameters
    {
        public Vector2 Direction;
        public float Power;
        /// <summary>Ponto de contacto horizontal na branca (-1 esquerda, +1 direita).</summary>
        public float SpinX;
        /// <summary>Ponto de contacto vertical na branca (-1 draw, +1 follow).</summary>
        public float SpinY;
        public float ImpulseScale;
        public float CurveAcceleration;
        public float CurveDuration;
        /// <summary>Desvio-padrão (graus) do erro aleatório aplicado pela autoridade.</summary>
        public float AngleNoise;

        public static ShotParameters FromInput(Vector2 direction, float power, float spinX, float spinY)
        {
            return new ShotParameters
            {
                Direction = direction,
                Power = power,
                SpinX = spinX,
                SpinY = spinY,
                ImpulseScale = 1f
            };
        }
    }

    /// <summary>PHY-012: todos os parâmetros de tacada passam aqui antes da simulação autoritativa.</summary>
    public static class ShotModifierPipeline
    {
        public static ShotParameters Apply(ShotParameters shot, IReadOnlyList<ModifierInstance> orderedModifiers)
        {
            if (orderedModifiers == null) return shot;
            foreach (var m in orderedModifiers)
            {
                foreach (var effect in m.Definition.Effects)
                {
                    switch (effect.Kind)
                    {
                        case ModifierEffectKind.ImpulseScale:
                            shot.ImpulseScale *= Mathf.Pow(effect.Value, m.Intensity);
                            break;
                        case ModifierEffectKind.CurveAcceleration:
                            shot.CurveAcceleration += effect.Value * m.Intensity;
                            shot.CurveDuration = Mathf.Max(shot.CurveDuration, effect.Duration);
                            break;
                        case ModifierEffectKind.ShotAngleNoise:
                            shot.AngleNoise += effect.Value * m.Intensity;
                            break;
                    }
                }
            }
            return shot;
        }
    }

    public struct CameraEffectProfile
    {
        public float Roll;
        public float Sway;
        public float AimSway;
        public float Vignette;

        public bool IsNeutral => Mathf.Approximately(Roll, 0f) && Sway <= 0f && AimSway <= 0f && Vignette <= 0f;
    }

    /// <summary>
    /// BaseCameraProfile → LocalCameraModifierPipeline → RenderedCamera. Derivado de estado autoritativo,
    /// aplicado só no cliente do jogador afectado. Em modo conforto a oscilação visual vira penalização de mira.
    /// </summary>
    public static class CameraModifierPipeline
    {
        public static CameraEffectProfile Build(IReadOnlyList<ModifierInstance> orderedModifiers, bool comfortMode)
        {
            var profile = new CameraEffectProfile();
            if (orderedModifiers == null) return profile;

            foreach (var m in orderedModifiers)
            {
                foreach (var effect in m.Definition.Effects)
                {
                    switch (effect.Kind)
                    {
                        case ModifierEffectKind.CameraRoll:
                            profile.Roll += effect.Value;
                            break;
                        case ModifierEffectKind.CameraSway:
                            profile.Sway = Mathf.Max(profile.Sway, effect.Value * m.Intensity);
                            break;
                        case ModifierEffectKind.AimSway:
                            profile.AimSway = Mathf.Max(profile.AimSway, effect.Value * m.Intensity);
                            break;
                        case ModifierEffectKind.CameraVignette:
                            profile.Vignette = Mathf.Max(profile.Vignette, effect.Value);
                            break;
                    }
                }
            }

            if (comfortMode && profile.Sway > 0f)
            {
                profile.AimSway = Mathf.Max(profile.AimSway, profile.Sway) * 1.4f;
                profile.Sway = 0f;
            }
            return profile;
        }
    }
}
