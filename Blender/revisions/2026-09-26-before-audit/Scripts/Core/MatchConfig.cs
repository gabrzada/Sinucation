using FriendSlop.Billiards;
using UnityEngine;

namespace FriendSlop.Core
{
    public enum RackMode
    {
        Adaptive,
        NineBall,
        SixBall,
        FourBall
    }

    /// <summary>
    /// Toda a configuração de partida (GAME-001, NFR-007). Nada disto está hardcoded na lógica.
    /// </summary>
    [CreateAssetMenu(menuName = "FriendSlop/Match Config", fileName = "MatchConfig")]
    public sealed class MatchConfig : ScriptableObject
    {
        /// <summary>Incrementar quando o significado de campos mudar (NFR-013).</summary>
        public const int ConfigVersion = 1;

        [Header("Jogadores")]
        public int PlayerMin = 3;
        public int PlayerMax = 6;

        [Header("Rondas e rack")]
        public int RoundLimit = 6;
        public RackMode RackMode = RackMode.Adaptive;
        [Tooltip("Se > 0, fixa o número de bolas da rack (3–15) e ignora o RackMode.")]
        public int FixedBallCount;
        [Tooltip("Bolas por rack em modo adaptativo, indexado pelo número de jogadores activos.")]
        public int[] AdaptiveBallsByActivePlayers = { 9, 9, 9, 9, 6, 4, 4 };
        public int SuddenDeathBalls = 4;
        [Tooltip("Tacadas por rack antes do procedimento de stalemate (rerack uma vez, depois resolução súbita).")]
        public int MaxShotsPerRack = 45;

        [Header("Turnos")]
        [Tooltip("Segundos para iniciar uma tacada. 0 = sem limite.")]
        public float TurnTimeLimit = 50f;

        [Header("Vidas e risco")]
        [Tooltip("Uma gosma = eliminado. Mantido por compatibilidade; o lobby força 1.")]
        public int StartingLives = 1;
        [Tooltip("Balas no revólver na primeira ronda.")]
        public int RiskDangerTokens = 1;
        [Tooltip("Tambores do revólver (o resto dá debuffs).")]
        public int RiskTotalTokens = 6;
        [Tooltip("Crescimento exponencial das balas por ronda: balas = base × crescimento^(ronda−1).")]
        public float RiskGrowth = 1.4f;
        public float PunishmentPresentationSeconds = 3.6f;
        public float TriggerTimeout = 12f;
        [Tooltip("Se verdadeiro, jogadores imunes a sabotagem também são excluídos do sorteio de punição.")]
        public bool ImmunityProtectsFromPunishment = false;

        [Header("Economia (Dinheiros)")]
        public string CurrencyName = "Dinheiros";
        public int WinnerReward = 100;
        public int ParticipationReward = 50;
        [Tooltip("Dinheiros por cada bola encaçapada legalmente.")]
        public int PocketReward = 10;
        public int CoinCap = 300;

        [Header("Sabotagem")]
        [Tooltip("Custo da sabotagem 'Travessura' (efeitos de tacada). A 'Caos' custa isto × ChaosCostMultiplier.")]
        public bool EnableSabotage = true;
        [Tooltip("Máquina de sabotagem aberta durante toda a partida (não só numa janela depois da roleta).")]
        public bool SabotageAnytime = true;
        [Tooltip("Recarga por comprador depois de cada sabotagem (modo sempre aberto).")]
        public float SabotageCooldownSeconds = 20f;
        [Tooltip("Tempo em que o alvo fica imune depois de ser sabotado (modo sempre aberto).")]
        public float SabotageImmunitySeconds = 15f;
        public int SabotageCost = 60;
        public float ChaosCostMultiplier = 2f;
        public int MaxPurchasesPerWindow = 1;
        [Tooltip("Janelas seguintes em que o alvo fica imune depois de ser sabotado (0 = só a janela actual).")]
        public int ImmunityWindowsAfterSabotage = 0;
        public float RepeatTargetCostMultiplier = 1.25f;
        [Tooltip("Desconto para quem tem menos vitórias quando o líder já vai à frente por UnderdogGap ou mais.")]
        public float UnderdogDiscount = 0.25f;
        public int UnderdogGap = 2;
        [Tooltip("Tempo para ir ao portátil e escolher o alvo.")]
        public float ShopWindowSeconds = 22f;

        [Header("Modificadores")]
        [Tooltip("Se falso, sobreviver à roleta não dá efeito e não há loja de sabotagem (partida 'limpa').")]
        public bool EnableDebuffs = true;
        public int MaxGameplayModifiersPerPlayer = 2;
        public float AntiRepeatWeightMultiplier = 0.5f;

        [Header("Ritmo de apresentação")]
        public float RoundIntroSeconds = 2f;
        public float ShotResultSeconds = 1.1f;
        public float RoundWonSeconds = 3f;
        public float RiskRevealSeconds = 3.2f;
        public float ShopRevealSeconds = 2.2f;

        [Header("Física")]
        public BilliardsPhysicsSettings Physics = new BilliardsPhysicsSettings();

        public int BallsForActivePlayers(int activePlayers)
        {
            if (FixedBallCount > 0) return Mathf.Clamp(FixedBallCount, 3, 15);
            switch (RackMode)
            {
                case RackMode.NineBall: return 9;
                case RackMode.SixBall: return 6;
                case RackMode.FourBall: return 4;
            }
            if (AdaptiveBallsByActivePlayers == null || AdaptiveBallsByActivePlayers.Length == 0) return 9;
            int index = Mathf.Clamp(activePlayers, 0, AdaptiveBallsByActivePlayers.Length - 1);
            return AdaptiveBallsByActivePlayers[index];
        }
    }
}
