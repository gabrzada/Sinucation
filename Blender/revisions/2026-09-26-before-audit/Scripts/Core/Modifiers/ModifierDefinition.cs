using System;
using UnityEngine;

namespace FriendSlop.Core.Modifiers
{
    public enum ModifierCategory
    {
        Shot,
        Camera
    }

    public enum ModifierDurationType
    {
        Shots,
        Turns,
        Rounds,
        Permanent
    }

    public enum StackPolicy
    {
        Stack,
        Refresh,
        Replace,
        Reject,
        MaxIntensity
    }

    /// <summary>
    /// Descritores genéricos de efeito. Os pipelines processam descritores, nunca IDs concretos de debuff,
    /// por isso o TurnManager/Director não sabe o que é "bêbado" ou "bola pesada".
    /// </summary>
    public enum ModifierEffectKind
    {
        /// <summary>Multiplica a conversão potência → impulso da branca (autoritativo).</summary>
        ImpulseScale,
        /// <summary>Aceleração lateral repetível da branca em m/s² (+ direita, − esquerda). Duration = segundos.</summary>
        CurveAcceleration,
        /// <summary>Rotação local da câmara em graus (apenas render no cliente afectado).</summary>
        CameraRoll,
        /// <summary>Oscilação visual da câmara (0..1). Removida em modo conforto.</summary>
        CameraSway,
        /// <summary>Oscilação da mira (0..1). Mantida em modo conforto como penalização não nauseante.</summary>
        AimSway,
        /// <summary>Desvio aleatório (graus, desvio-padrão) aplicado pela autoridade à direcção da tacada.</summary>
        ShotAngleNoise,
        /// <summary>Vinheta escura local (0..1) — "visão de túnel". Só render no cliente afectado.</summary>
        CameraVignette
    }

    /// <summary>Nível de sabotagem que o comprador escolhe: barata (efeitos de tacada) ou caótica (câmara).</summary>
    public enum SabotageTier
    {
        Prank = 0,
        Chaos = 1
    }

    [Serializable]
    public struct ModifierEffect
    {
        public ModifierEffectKind Kind;
        public float Value;
        public float Duration;

        public ModifierEffect(ModifierEffectKind kind, float value, float duration = 0f)
        {
            Kind = kind;
            Value = value;
            Duration = duration;
        }
    }

    /// <summary>DEBUFF-001: definição de dados de um modificador.</summary>
    [CreateAssetMenu(menuName = "FriendSlop/Modifier Definition", fileName = "Modifier")]
    public sealed class ModifierDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        [TextArea] public string Description;
        public string ShortLabel;
        public ModifierCategory Category;

        [Header("Duração")]
        public ModifierDurationType DurationType = ModifierDurationType.Shots;
        [Min(1)] public int DurationValue = 1;

        [Header("Sorteio")]
        public float ProbabilityWeight = 50f;
        [Tooltip("Apenas informativo; a lógica nunca depende da raridade (DEBUFF-010).")]
        public string RarityTag = "Comum";
        [Tooltip("Em que nível de sabotagem este efeito pode sair (o risco sorteia de todos).")]
        public SabotageTier Tier = SabotageTier.Prank;

        [Header("Resolução")]
        public StackPolicy StackPolicy = StackPolicy.Refresh;
        [Min(1)] public int MaxIntensity = 1;
        public string ConflictGroup;
        public int Priority;
        public bool CountsTowardGameplayLimit = true;

        [Header("Apresentação")]
        public Color Tint = Color.white;

        public ModifierEffect[] Effects = Array.Empty<ModifierEffect>();
    }
}
