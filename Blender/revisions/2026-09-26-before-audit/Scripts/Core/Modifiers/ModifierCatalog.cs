using System.Collections.Generic;
using UnityEngine;

namespace FriendSlop.Core.Modifiers
{
    /// <summary>
    /// Catálogo de efeitos. Travessuras (baratas) mexem na tacada mas deixam compensar com habilidade;
    /// Caos (caras) mexem na câmara/percepção. O risco pós-ronda sorteia de ambos.
    /// Também serve de fonte para o menu FriendSlop ▸ Gerar assets.
    /// </summary>
    public static class ModifierCatalog
    {
        public const string CurveRight = "curve_right";
        public const string CurveLeft = "curve_left";
        public const string HeavyBall = "heavy_ball";
        public const string ShakyAim = "shaky_aim";
        public const string SlipperyCue = "slippery_cue";
        public const string UpsideDown = "upside_down";
        public const string SpiderMode = "spider_mode";
        public const string Drunk = "drunk";
        public const string TunnelVision = "tunnel_vision";

        public const string ShotCurveGroup = "ShotCurve";
        public const string StrongCameraGroup = "StrongCamera";

        public static List<ModifierDefinition> CreateDefaults()
        {
            return new List<ModifierDefinition>
            {
                Make(CurveRight, "Curva Direita", "A tua próxima tacada curva para a direita. Compensa a mira.", "CURVA >",
                    SabotageTier.Prank, StackPolicy.Replace, 1, ShotCurveGroup, 10, 45f, "Comum", new Color(1f, 0.6f, 0.2f),
                    new ModifierEffect(ModifierEffectKind.CurveAcceleration, 0.9f, 1.6f)),

                Make(CurveLeft, "Curva Esquerda", "A tua próxima tacada curva para a esquerda. Compensa a mira.", "< CURVA",
                    SabotageTier.Prank, StackPolicy.Replace, 1, ShotCurveGroup, 10, 45f, "Comum", new Color(0.3f, 0.75f, 1f),
                    new ModifierEffect(ModifierEffectKind.CurveAcceleration, -0.9f, 1.6f)),

                Make(HeavyBall, "Bola Pesada", "A tua próxima tacada sai com muito menos força. Recua mais o taco.", "PESADA",
                    SabotageTier.Prank, StackPolicy.MaxIntensity, 2, null, 20, 45f, "Comum", new Color(0.6f, 0.6f, 0.65f),
                    new ModifierEffect(ModifierEffectKind.ImpulseScale, 0.62f)),

                Make(ShakyAim, "Mira Tremida", "A tua mira treme durante a próxima tacada. Espera pelo momento certo.", "TREMIDA",
                    SabotageTier.Prank, StackPolicy.Refresh, 1, null, 25, 35f, "Comum", new Color(0.95f, 0.85f, 0.35f),
                    new ModifierEffect(ModifierEffectKind.AimSway, 0.7f)),

                Make(SlipperyCue, "Taco Ensaboado", "O taco escorrega: a próxima tacada sai com um pequeno desvio aleatório.", "ENSABOADO",
                    SabotageTier.Prank, StackPolicy.MaxIntensity, 2, null, 26, 35f, "Comum", new Color(0.55f, 0.95f, 0.95f),
                    new ModifierEffect(ModifierEffectKind.ShotAngleNoise, 2.2f)),

                Make(UpsideDown, "Mundo de Cabeça para Baixo", "A tua câmara fica invertida durante a próxima tacada.", "INVERTIDO",
                    SabotageTier.Chaos, StackPolicy.Replace, 1, StrongCameraGroup, 30, 30f, "Incomum", new Color(0.8f, 0.4f, 1f),
                    new ModifierEffect(ModifierEffectKind.CameraRoll, 180f)),

                Make(SpiderMode, "Modo Aranha", "A tua câmara roda 90° durante a próxima tacada.", "ARANHA",
                    SabotageTier.Chaos, StackPolicy.Replace, 1, StrongCameraGroup, 30, 30f, "Incomum", new Color(1f, 0.3f, 0.35f),
                    new ModifierEffect(ModifierEffectKind.CameraRoll, 90f)),

                Make(Drunk, "Modo Bêbado", "A câmara e a mira oscilam durante a próxima tacada.", "BÊBADO",
                    SabotageTier.Chaos, StackPolicy.Replace, 1, StrongCameraGroup, 30, 20f, "Raro", new Color(0.45f, 0.9f, 0.45f),
                    new ModifierEffect(ModifierEffectKind.CameraSway, 1f),
                    new ModifierEffect(ModifierEffectKind.AimSway, 1f)),

                Make(TunnelVision, "Visão de Túnel", "Só vês o centro do ecrã durante a próxima tacada.", "TÚNEL",
                    SabotageTier.Chaos, StackPolicy.Replace, 1, StrongCameraGroup, 30, 25f, "Incomum", new Color(0.3f, 0.3f, 0.35f),
                    new ModifierEffect(ModifierEffectKind.CameraVignette, 0.72f)),
            };
        }

        static ModifierDefinition Make(string id, string name, string description, string shortLabel, SabotageTier tier,
            StackPolicy stackPolicy, int maxIntensity, string conflictGroup, int priority, float weight, string rarity, Color tint,
            params ModifierEffect[] effects)
        {
            var def = ScriptableObject.CreateInstance<ModifierDefinition>();
            def.name = id;
            def.Id = id;
            def.DisplayName = name;
            def.Description = description;
            def.ShortLabel = shortLabel;
            def.Category = tier == SabotageTier.Chaos ? ModifierCategory.Camera : ModifierCategory.Shot;
            def.Tier = tier;
            def.DurationType = ModifierDurationType.Shots;
            def.DurationValue = 1;
            def.StackPolicy = stackPolicy;
            def.MaxIntensity = maxIntensity;
            def.ConflictGroup = conflictGroup;
            def.Priority = priority;
            def.ProbabilityWeight = weight;
            def.RarityTag = rarity;
            def.Tint = tint;
            def.Effects = effects;
            return def;
        }
    }
}
