using System;
using UnityEngine;

namespace FriendSlop.Billiards
{
    /// <summary>Parâmetros calibráveis da física de bilhar (unidades em metros e segundos).</summary>
    [Serializable]
    public sealed class BilliardsPhysicsSettings
    {
        [Header("Mesa")]
        public float TableLength = 2.24f;
        public float TableWidth = 1.12f;
        public float BallRadius = 0.028575f;
        [Tooltip("Abertura das tabelas junto às caçapas de canto, medida a partir do canto.")]
        public float CornerGap = 0.075f;
        [Tooltip("Meia abertura das caçapas laterais.")]
        public float SideGap = 0.062f;
        public float CornerPocketRadius = 0.056f;
        public float CornerPocketOffset = 0.016f;
        public float SidePocketRadius = 0.05f;
        public float SidePocketOffset = 0.034f;

        [Header("Simulação")]
        public float FixedStep = 1f / 480f;
        public int MaxStepsPerFrame = 80;
        public float MaxSimulationSeconds = 25f;
        public float MaxBallSpeed = 7f;

        [Header("Atrito e restituição")]
        public float RollingDeceleration = 0.32f;
        public float LinearDrag = 0.12f;
        public float StopSpeed = 0.012f;
        public float BallRestitution = 0.94f;
        public float CushionRestitution = 0.76f;
        public float CushionFriction = 0.08f;

        [Header("Taco")]
        public float MinCueSpeed = 0.25f;
        public float MaxCueSpeed = 5.2f;
        public float PowerCurveExponent = 1.35f;

        [Header("Efeito (spin)")]
        public float FollowFactor = 0.55f;
        public float EnglishFactor = 0.3f;
        public float EnglishCushionTransfer = 0.6f;
        public float SpinDecayPerSecond = 0.9f;

        /// <summary>PHY-004: curva configurável de potência normalizada para velocidade da branca.</summary>
        public float CueSpeedForPower(float power)
        {
            power = Mathf.Clamp01(power);
            return Mathf.Lerp(MinCueSpeed, MaxCueSpeed, Mathf.Pow(power, PowerCurveExponent));
        }
    }
}
