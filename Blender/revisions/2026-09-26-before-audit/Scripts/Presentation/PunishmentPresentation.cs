using FriendSlop.Match;
using FriendSlop.Net;
using UnityEngine;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// UI-007: a animação do sorteio é derivada do jogador já escolhido pela autoridade.
    /// Desacelera e termina sempre nele; nunca decide nada e é igual em todos os clientes.
    /// </summary>
    public static class PunishmentPresentation
    {
        const float LandAt = 0.82f;

        public static int HighlightedCandidate(MatchView view)
        {
            var candidates = view.Candidates;
            if (candidates.Count == 0) return -1;
            if (view.Phase != MatchPhase.PunishmentDraw) return view.Selected;

            int selectedIndex = Mathf.Max(0, candidates.IndexOf(view.Selected));
            if (candidates.Count == 1) return candidates[0];

            int totalSteps = candidates.Count * 3 + selectedIndex;
            float elapsed = view.PhaseDuration - view.PhaseRemaining;
            float t = view.PhaseDuration > 0f ? Mathf.Clamp01(elapsed / (view.PhaseDuration * LandAt)) : 1f;
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            int step = Mathf.Min(totalSteps, Mathf.FloorToInt(eased * totalSteps));
            return candidates[step % candidates.Count];
        }
    }
}
