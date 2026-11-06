using System;
using System.Collections.Generic;
using FriendSlop.Core.Modifiers;
using UnityEngine;

namespace FriendSlop.Core
{
    public enum Archetype
    {
        Triangle,
        Circle,
        Square
    }

    /// <summary>Identidade visual escolhida no lobby. Nunca carrega atributos competitivos (CHAR-003, COS-004).</summary>
    [Serializable]
    public sealed class PlayerProfile
    {
        public string Name;
        public Archetype Archetype;
        public Color Color;
        /// <summary>Slots cosméticos (COS-001). Índices no CosmeticCatalog; 0 = nenhum.</summary>
        public int HatId;
        public int AccessoryId;
        public bool IsBot;
        /// <summary>Vitórias de partida no histórico do jogador (só para mostrar no cartão).</summary>
        public int Wins;

        public PlayerProfile(string name, Archetype archetype, Color color)
        {
            Name = name;
            Archetype = archetype;
            Color = color;
        }

        public PlayerProfile Clone()
        {
            return new PlayerProfile(Name, Archetype, Color) { HatId = HatId, AccessoryId = AccessoryId, IsBot = IsBot, Wins = Wins };
        }
    }

    /// <summary>Estado autoritativo de um jogador dentro de uma partida.</summary>
    public sealed class PlayerState
    {
        readonly List<string> _recentModifierIds = new List<string>();

        public PlayerState(int id, PlayerProfile profile, int startingLives)
        {
            Id = id;
            Profile = profile;
            Lives = startingLives;
        }

        public int Id { get; }
        public PlayerProfile Profile { get; }
        public string Name => Profile.Name;
        public ModifierController Modifiers { get; } = new ModifierController();

        public int Lives;
        public bool Eliminated;
        public int EliminatedRound = -1;

        public int Coins;
        public int CoinsEarned;
        public int CoinsSpent;
        public int CoinsLostToCap;

        public int RoundWins;
        public int LegalPockets;
        public int Fouls;
        public int ShotsTaken;
        public int SabotagesDone;
        public int SabotagesReceived;
        public int TimesSelectedForRisk;

        public int ImmuneUntilWindow = -1;
        public int LastTargetId = -1;
        public int LastTargetWindow = -1;

        public bool IsActive => !Eliminated;
        public IReadOnlyList<string> RecentModifierIds => _recentModifierIds;

        public bool IsImmune(int window) => ImmuneUntilWindow >= window;

        public void RememberModifier(string id)
        {
            _recentModifierIds.Add(id);
            if (_recentModifierIds.Count > 2) _recentModifierIds.RemoveAt(0);
        }
    }
}
