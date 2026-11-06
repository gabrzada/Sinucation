using System.Collections.Generic;
using System.IO;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using UnityEngine;

namespace FriendSlop.Net
{
    public enum SessionKind : byte
    {
        Offline,
        Direct,
        Online
    }

    /// <summary>Regras editáveis pelo anfitrião no lobby (LOB-005). Aplicadas a uma cópia da MatchConfig.</summary>
    public sealed class LobbySettings
    {
        public int RoundLimit = 6;
        /// <summary>Crescimento das balas por ronda em percentagem (140 = ×1,4).</summary>
        public int RiskGrowthPct = 140;
        public RackMode RackMode = RackMode.Adaptive;
        public int RiskDanger = 1;
        public int RiskTotal = 6;
        public int WinnerReward = 100;
        public int ParticipationReward = 50;
        public int SabotageCost = 100;
        public int CoinCap = 250;
        public int ShopSeconds = 22;
        public int TurnSeconds = 50;
        /// <summary>0 = automático (depende do nº de jogadores); senão 3–15 bolas.</summary>
        public int BallCount;
        public int MaxPlayers = 6;
        public bool EnableSabotage = true;
        public bool EnableDebuffs = true;
        public int SabotageCooldown = 20;

        public static LobbySettings From(MatchConfig c)
        {
            return new LobbySettings
            {
                BallCount = c.FixedBallCount,
                MaxPlayers = c.PlayerMax,
                EnableSabotage = c.EnableSabotage,
                EnableDebuffs = c.EnableDebuffs,
                SabotageCooldown = Mathf.RoundToInt(c.SabotageCooldownSeconds),
                RoundLimit = c.RoundLimit,
                RiskGrowthPct = Mathf.RoundToInt(c.RiskGrowth * 100f),
                RackMode = c.RackMode,
                RiskDanger = c.RiskDangerTokens,
                RiskTotal = c.RiskTotalTokens,
                WinnerReward = c.WinnerReward,
                ParticipationReward = c.ParticipationReward,
                SabotageCost = c.SabotageCost,
                CoinCap = c.CoinCap,
                ShopSeconds = Mathf.RoundToInt(c.ShopWindowSeconds),
                TurnSeconds = Mathf.RoundToInt(c.TurnTimeLimit)
            };
        }

        public void Clamp()
        {
            RoundLimit = Mathf.Clamp(RoundLimit, 1, 12);
            RiskGrowthPct = Mathf.Clamp(RiskGrowthPct, 100, 250);
            RiskTotal = Mathf.Clamp(RiskTotal, 2, 12);
            RiskDanger = Mathf.Clamp(RiskDanger, 0, RiskTotal);
            WinnerReward = Mathf.Clamp(WinnerReward, 0, 1000);
            ParticipationReward = Mathf.Clamp(ParticipationReward, 0, 1000);
            SabotageCost = Mathf.Clamp(SabotageCost, 1, 2000);
            CoinCap = Mathf.Clamp(CoinCap, 50, 5000);
            ShopSeconds = Mathf.Clamp(ShopSeconds, 5, 60);
            TurnSeconds = Mathf.Clamp(TurnSeconds, 0, 180);
            BallCount = BallCount <= 0 ? 0 : Mathf.Clamp(BallCount, 3, 15);
            MaxPlayers = Mathf.Clamp(MaxPlayers, 3, 6);
            SabotageCooldown = Mathf.Clamp(SabotageCooldown, 5, 90);
        }

        public void ApplyTo(MatchConfig c)
        {
            Clamp();
            c.RoundLimit = RoundLimit;
            c.StartingLives = 1;
            c.RiskGrowth = RiskGrowthPct / 100f;
            c.RackMode = RackMode;
            c.RiskDangerTokens = RiskDanger;
            c.RiskTotalTokens = RiskTotal;
            c.WinnerReward = WinnerReward;
            c.ParticipationReward = ParticipationReward;
            c.SabotageCost = SabotageCost;
            c.CoinCap = CoinCap;
            c.ShopWindowSeconds = ShopSeconds;
            c.TurnTimeLimit = TurnSeconds;
            c.FixedBallCount = BallCount;
            c.EnableSabotage = EnableSabotage;
            c.EnableDebuffs = EnableDebuffs;
            c.SabotageCooldownSeconds = SabotageCooldown;
        }

        public void Write(BinaryWriter w)
        {
            w.Write((byte)RoundLimit);
            w.Write((byte)RiskGrowthPct);
            w.Write((byte)RackMode);
            w.Write((byte)RiskDanger);
            w.Write((byte)RiskTotal);
            w.Write((short)WinnerReward);
            w.Write((short)ParticipationReward);
            w.Write((short)SabotageCost);
            w.Write((short)CoinCap);
            w.Write((byte)ShopSeconds);
            w.Write((byte)TurnSeconds);
            w.Write((byte)BallCount);
            w.Write((byte)MaxPlayers);
            w.Write(EnableSabotage);
            w.Write(EnableDebuffs);
            w.Write((byte)SabotageCooldown);
        }

        public void Read(BinaryReader r)
        {
            RoundLimit = r.ReadByte();
            RiskGrowthPct = r.ReadByte();
            RackMode = (RackMode)r.ReadByte();
            RiskDanger = r.ReadByte();
            RiskTotal = r.ReadByte();
            WinnerReward = r.ReadInt16();
            ParticipationReward = r.ReadInt16();
            SabotageCost = r.ReadInt16();
            CoinCap = r.ReadInt16();
            ShopSeconds = r.ReadByte();
            TurnSeconds = r.ReadByte();
            BallCount = r.ReadByte();
            MaxPlayers = r.ReadByte();
            EnableSabotage = r.ReadBoolean();
            EnableDebuffs = r.ReadBoolean();
            SabotageCooldown = r.ReadByte();
        }

        public LobbySettings Copy()
        {
            var c = new LobbySettings();
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                Write(w);
                w.Flush();
                ms.Position = 0;
                using (var r = new BinaryReader(ms)) c.Read(r);
            }
            return c;
        }

        public bool SameAs(LobbySettings o)
        {
            return RoundLimit == o.RoundLimit && RiskGrowthPct == o.RiskGrowthPct && RackMode == o.RackMode && RiskDanger == o.RiskDanger &&
                   RiskTotal == o.RiskTotal && WinnerReward == o.WinnerReward && ParticipationReward == o.ParticipationReward &&
                   SabotageCost == o.SabotageCost && CoinCap == o.CoinCap && ShopSeconds == o.ShopSeconds && TurnSeconds == o.TurnSeconds &&
                   BallCount == o.BallCount && MaxPlayers == o.MaxPlayers && EnableSabotage == o.EnableSabotage && EnableDebuffs == o.EnableDebuffs && SabotageCooldown == o.SabotageCooldown;
        }
    }

    public sealed class SlotView
    {
        public int SlotId;
        public ulong ClientId;
        public bool Ready;
        public bool Connected;
        public PlayerProfile Profile = new PlayerProfile("", Archetype.Circle, Color.white);
    }

    public struct ModView
    {
        public int Def;
        public int Remaining;
        public int Intensity;
        public int Source;
        public ModifierDurationType DurationType;
    }

    public sealed class PlayerView
    {
        public int Id;
        public int SlotId;
        public ulong ClientId;
        public bool Connected;
        public PlayerProfile Profile = new PlayerProfile("", Archetype.Circle, Color.white);
        public int Lives;
        public bool Eliminated;
        public int EliminatedRound;
        public int Coins;
        public int RoundWins;
        public int LegalPockets;
        public int Fouls;
        public int SabotagesDone;
        public int SabotagesReceived;
        public int CoinsEarned;
        public int CoinsSpent;
        public bool Immune;
        public bool ShopPassed;
        public int PurchasesThisWindow;
        /// <summary>Segundos até este jogador poder voltar a usar a máquina (modo sempre aberto).</summary>
        public float SabotageCooldown;
        /// <summary>Segundos de imunidade restantes depois de ter sido sabotado.</summary>
        public float ImmuneFor;
        public readonly List<ModView> Mods = new List<ModView>();

        public bool IsBot => Profile.IsBot;
        public bool IsActive => !Eliminated;
        public string Name => Profile.Name;
    }

    public struct PurchaseView
    {
        public int Buyer;
        public int Target;
        public int Def;
        public int Tier;
    }

    /// <summary>Estado da jukebox partilhada (fila tipo bot de Discord, autoritativa no anfitrião).</summary>
    public sealed class MusicView
    {
        public int Track = -1;
        public double StartedAt;
        public bool Paused;
        public readonly List<int> Queue = new List<int>();
        public readonly List<ulong> QueuedBy = new List<ulong>();
        public int SkipVotes;
        public int SkipNeeded = 1;

        public void Write(BinaryWriter w)
        {
            w.Write((sbyte)Track);
            w.Write(StartedAt);
            w.Write(Paused);
            w.Write((byte)Queue.Count);
            for (int i = 0; i < Queue.Count; i++)
            {
                w.Write((byte)Queue[i]);
                w.Write(i < QueuedBy.Count ? QueuedBy[i] : 0UL);
            }
            w.Write((byte)SkipVotes);
            w.Write((byte)SkipNeeded);
        }

        public void Read(BinaryReader r)
        {
            Track = r.ReadSByte();
            StartedAt = r.ReadDouble();
            Paused = r.ReadBoolean();
            Queue.Clear();
            QueuedBy.Clear();
            int n = r.ReadByte();
            for (int i = 0; i < n; i++)
            {
                Queue.Add(r.ReadByte());
                QueuedBy.Add(r.ReadUInt64());
            }
            SkipVotes = r.ReadByte();
            SkipNeeded = r.ReadByte();
        }
    }

    public sealed class TelemetryView
    {
        public int Shots;
        public int Fouls;
        public int Timeouts;
        public int RiskEvents;
        public int Discharges;
        public int Sabotages;
        public float Duration;
        public readonly int[] Buckets = new int[3];
        public readonly List<KeyValuePair<int, int>> ModifierCounts = new List<KeyValuePair<int, int>>();

        public float Share(int bucket) => Shots == 0 ? 0f : Buckets[bucket] / (float)Shots;
    }

    /// <summary>
    /// Snapshot completo do estado autoritativo (MULT-008). O host constrói-o a partir do director;
    /// os clientes recebem-no por rede. Toda a apresentação lê daqui — nunca do director.
    /// </summary>
    public sealed class MatchView
    {
        public const int MaxSeats = 8;

        public const int Tiers = 2;

        public SessionKind Kind;
        public string JoinCode = "";
        public string RoomName = "";
        public bool IsPrivate;
        public ulong HostClientId;
        public bool InMatch;
        public readonly MusicView Music = new MusicView();
        public readonly List<SlotView> Slots = new List<SlotView>();
        public readonly LobbySettings Settings = new LobbySettings();

        public MatchPhase Phase;
        public float PhaseDuration;
        public float PhaseRemaining;
        public int RoundIndex;
        public int RoundLimit;
        public bool SuddenDeath;
        public int CurrentPlayer = -1;
        public bool BallInHand;
        public bool KitchenOnly;
        public int ShotSequence;
        public int FinalBall;
        public readonly List<int> RoundOrder = new List<int>();
        public readonly List<int> BaseOrder = new List<int>();
        public int RoundWinner = -1;
        public readonly List<int> Candidates = new List<int>();
        public int Selected = -1;
        public string RiskEventId = "";
        public bool HasRiskResult;
        public RiskOutcome RiskOutcome;
        public int RiskModifierDef = -1;
        public bool RiskEliminated;
        public bool RiskAutomatic;
        public int DryStreak;
        /// <summary>Balas no revólver nesta ronda e número de tambores.</summary>
        public int LoadedChambers = 1;
        public int Chambers = 6;
        public int ShopWindow;
        public bool ShopOpen;
        /// <summary>Máquina de sabotagem aberta durante toda a partida.</summary>
        public bool SabotageAnytime;
        public int MaxPurchases = 1;
        public readonly List<PurchaseView> Purchases = new List<PurchaseView>();
        public int MatchWinner = -1;
        public readonly List<int> Ranking = new List<int>();
        public int WinnerReward;
        public int ParticipationReward;
        public int CoinCap;
        public readonly List<PlayerView> Players = new List<PlayerView>();
        public readonly SabotageBlock[] ShopBlock = new SabotageBlock[Tiers * MaxSeats * MaxSeats];
        public readonly int[] ShopCost = new int[Tiers * MaxSeats * MaxSeats];
        public readonly TelemetryView Telemetry = new TelemetryView();
        public readonly List<BallState> Balls = new List<BallState>();

        public PlayerView GetPlayer(int id)
        {
            foreach (var p in Players)
                if (p.Id == id) return p;
            return null;
        }

        public PlayerView PlayerForClient(ulong clientId)
        {
            foreach (var p in Players)
                if (!p.IsBot && p.ClientId == clientId) return p;
            return null;
        }

        public SlotView SlotForClient(ulong clientId)
        {
            foreach (var s in Slots)
                if (!s.Profile.IsBot && s.ClientId == clientId) return s;
            return null;
        }

        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (var p in Players)
                    if (p.IsActive) count++;
                return count;
            }
        }

        public int LowestBall
        {
            get
            {
                int lowest = -1;
                foreach (var b in Balls)
                    if (b.Number > 0 && !b.Pocketed && (lowest < 0 || b.Number < lowest)) lowest = b.Number;
                return lowest;
            }
        }

        public int ObjectBallsOnTable
        {
            get
            {
                int count = 0;
                foreach (var b in Balls)
                    if (b.Number > 0 && !b.Pocketed) count++;
                return count;
            }
        }

        public static int ShopIndex(int tier, int buyer, int target) => (tier * MaxSeats + buyer) * MaxSeats + target;
        public SabotageBlock BlockFor(int buyer, int target, int tier = 0) => ShopBlock[ShopIndex(tier, buyer, target)];
        public int CostFor(int buyer, int target, int tier = 0) => ShopCost[ShopIndex(tier, buyer, target)];
    }

    public static class MatchViewCodec
    {
        public static byte[] Serialize(MatchView v)
        {
            using (var ms = new MemoryStream(1024))
            using (var w = new BinaryWriter(ms))
            {
                Write(v, w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static void Deserialize(byte[] data, MatchView v)
        {
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
                Read(r, v);
        }

        public static void WriteProfile(BinaryWriter w, PlayerProfile p)
        {
            w.Write(p.Name ?? "");
            w.Write((byte)p.Archetype);
            Color32 c = p.Color;
            w.Write(c.r);
            w.Write(c.g);
            w.Write(c.b);
            w.Write((byte)p.HatId);
            w.Write((byte)p.AccessoryId);
            w.Write(p.IsBot);
            w.Write((ushort)Mathf.Clamp(p.Wins, 0, 65535));
        }

        public static void ReadProfile(BinaryReader r, PlayerProfile p)
        {
            p.Name = r.ReadString();
            p.Archetype = (Archetype)r.ReadByte();
            p.Color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), 255);
            p.HatId = r.ReadByte();
            p.AccessoryId = r.ReadByte();
            p.IsBot = r.ReadBoolean();
            p.Wins = r.ReadUInt16();
        }

        public static void WriteBalls(BinaryWriter w, List<BallState> balls)
        {
            w.Write((byte)balls.Count);
            foreach (var b in balls)
            {
                w.Write((byte)b.Number);
                w.Write(b.Position.x);
                w.Write(b.Position.y);
                w.Write((sbyte)(b.Pocketed ? b.PocketIndex : -2));
            }
        }

        public static void ReadBalls(BinaryReader r, List<BallState> balls)
        {
            balls.Clear();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++)
            {
                int number = r.ReadByte();
                var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
                int pocket = r.ReadSByte();
                balls.Add(new BallState { Number = number, Position = pos, Pocketed = pocket != -2, PocketIndex = pocket == -2 ? -1 : pocket });
            }
        }

        static void WriteInts(BinaryWriter w, List<int> list)
        {
            w.Write((byte)list.Count);
            foreach (int i in list) w.Write((sbyte)i);
        }

        static void ReadInts(BinaryReader r, List<int> list)
        {
            list.Clear();
            int count = r.ReadByte();
            for (int i = 0; i < count; i++) list.Add(r.ReadSByte());
        }

        public static void Write(MatchView v, BinaryWriter w)
        {
            w.Write((byte)v.Kind);
            w.Write(v.JoinCode ?? "");
            w.Write(v.RoomName ?? "");
            w.Write(v.IsPrivate);
            w.Write(v.HostClientId);
            w.Write(v.InMatch);
            v.Music.Write(w);

            w.Write((byte)v.Slots.Count);
            foreach (var s in v.Slots)
            {
                w.Write((byte)s.SlotId);
                w.Write(s.ClientId);
                w.Write(s.Ready);
                w.Write(s.Connected);
                WriteProfile(w, s.Profile);
            }
            v.Settings.Write(w);

            if (!v.InMatch)
            {
                WriteBalls(w, v.Balls);
                return;
            }

            w.Write((byte)v.Phase);
            w.Write(v.PhaseDuration);
            w.Write(v.PhaseRemaining);
            w.Write((byte)v.RoundIndex);
            w.Write((byte)v.RoundLimit);
            w.Write(v.SuddenDeath);
            w.Write((sbyte)v.CurrentPlayer);
            w.Write(v.BallInHand);
            w.Write(v.KitchenOnly);
            w.Write(v.ShotSequence);
            w.Write((byte)v.FinalBall);
            WriteInts(w, v.RoundOrder);
            WriteInts(w, v.BaseOrder);
            w.Write((sbyte)v.RoundWinner);
            WriteInts(w, v.Candidates);
            w.Write((sbyte)v.Selected);
            w.Write(v.RiskEventId ?? "");
            w.Write(v.HasRiskResult);
            w.Write((byte)v.RiskOutcome);
            w.Write((short)v.RiskModifierDef);
            w.Write(v.RiskEliminated);
            w.Write(v.RiskAutomatic);
            w.Write((byte)v.DryStreak);
            w.Write((byte)v.LoadedChambers);
            w.Write((byte)v.Chambers);
            w.Write((byte)v.ShopWindow);
            w.Write(v.ShopOpen);
            w.Write(v.SabotageAnytime);
            w.Write((byte)v.MaxPurchases);
            w.Write((byte)v.Purchases.Count);
            foreach (var p in v.Purchases)
            {
                w.Write((byte)p.Buyer);
                w.Write((byte)p.Target);
                w.Write((short)p.Def);
                w.Write((byte)p.Tier);
            }
            w.Write((sbyte)v.MatchWinner);
            WriteInts(w, v.Ranking);
            w.Write((short)v.WinnerReward);
            w.Write((short)v.ParticipationReward);
            w.Write((short)v.CoinCap);

            w.Write((byte)v.Players.Count);
            foreach (var p in v.Players)
            {
                w.Write((byte)p.Id);
                w.Write((byte)p.SlotId);
                w.Write(p.ClientId);
                w.Write(p.Connected);
                WriteProfile(w, p.Profile);
                w.Write((byte)p.Lives);
                w.Write(p.Eliminated);
                w.Write((sbyte)p.EliminatedRound);
                w.Write((short)p.Coins);
                w.Write((byte)p.RoundWins);
                w.Write((short)p.LegalPockets);
                w.Write((short)p.Fouls);
                w.Write((byte)p.SabotagesDone);
                w.Write((byte)p.SabotagesReceived);
                w.Write((short)p.CoinsEarned);
                w.Write((short)p.CoinsSpent);
                w.Write(p.Immune);
                w.Write(p.ShopPassed);
                w.Write((byte)p.PurchasesThisWindow);
            w.Write((byte)Mathf.Clamp(Mathf.CeilToInt(p.SabotageCooldown * 4f), 0, 255));
            w.Write((byte)Mathf.Clamp(Mathf.CeilToInt(p.ImmuneFor * 4f), 0, 255));
                w.Write((byte)p.Mods.Count);
                foreach (var m in p.Mods)
                {
                    w.Write((short)m.Def);
                    w.Write((byte)m.Remaining);
                    w.Write((byte)m.Intensity);
                    w.Write((sbyte)m.Source);
                    w.Write((byte)m.DurationType);
                }
            }

            int n = v.Players.Count;
            for (int tier = 0; tier < MatchView.Tiers; tier++)
                for (int b = 0; b < n; b++)
                    for (int t = 0; t < n; t++)
                    {
                        int i = MatchView.ShopIndex(tier, b, t);
                        w.Write((byte)v.ShopBlock[i]);
                        w.Write((short)v.ShopCost[i]);
                    }

            var tel = v.Telemetry;
            w.Write((short)tel.Shots);
            w.Write((short)tel.Fouls);
            w.Write((short)tel.Timeouts);
            w.Write((short)tel.RiskEvents);
            w.Write((short)tel.Discharges);
            w.Write((short)tel.Sabotages);
            w.Write(tel.Duration);
            for (int i = 0; i < 3; i++) w.Write((short)tel.Buckets[i]);
            w.Write((byte)tel.ModifierCounts.Count);
            foreach (var kv in tel.ModifierCounts)
            {
                w.Write((short)kv.Key);
                w.Write((short)kv.Value);
            }

            WriteBalls(w, v.Balls);
        }

        public static void Read(BinaryReader r, MatchView v)
        {
            v.Kind = (SessionKind)r.ReadByte();
            v.JoinCode = r.ReadString();
            v.RoomName = r.ReadString();
            v.IsPrivate = r.ReadBoolean();
            v.HostClientId = r.ReadUInt64();
            v.InMatch = r.ReadBoolean();
            v.Music.Read(r);

            int slots = r.ReadByte();
            while (v.Slots.Count < slots) v.Slots.Add(new SlotView());
            while (v.Slots.Count > slots) v.Slots.RemoveAt(v.Slots.Count - 1);
            foreach (var s in v.Slots)
            {
                s.SlotId = r.ReadByte();
                s.ClientId = r.ReadUInt64();
                s.Ready = r.ReadBoolean();
                s.Connected = r.ReadBoolean();
                ReadProfile(r, s.Profile);
            }
            v.Settings.Read(r);

            if (!v.InMatch)
            {
                v.Players.Clear();
                v.Phase = MatchPhase.None;
                ReadBalls(r, v.Balls);
                return;
            }

            v.Phase = (MatchPhase)r.ReadByte();
            v.PhaseDuration = r.ReadSingle();
            v.PhaseRemaining = r.ReadSingle();
            v.RoundIndex = r.ReadByte();
            v.RoundLimit = r.ReadByte();
            v.SuddenDeath = r.ReadBoolean();
            v.CurrentPlayer = r.ReadSByte();
            v.BallInHand = r.ReadBoolean();
            v.KitchenOnly = r.ReadBoolean();
            v.ShotSequence = r.ReadInt32();
            v.FinalBall = r.ReadByte();
            ReadInts(r, v.RoundOrder);
            ReadInts(r, v.BaseOrder);
            v.RoundWinner = r.ReadSByte();
            ReadInts(r, v.Candidates);
            v.Selected = r.ReadSByte();
            v.RiskEventId = r.ReadString();
            v.HasRiskResult = r.ReadBoolean();
            v.RiskOutcome = (RiskOutcome)r.ReadByte();
            v.RiskModifierDef = r.ReadInt16();
            v.RiskEliminated = r.ReadBoolean();
            v.RiskAutomatic = r.ReadBoolean();
            v.DryStreak = r.ReadByte();
            v.LoadedChambers = r.ReadByte();
            v.Chambers = r.ReadByte();
            v.ShopWindow = r.ReadByte();
            v.ShopOpen = r.ReadBoolean();
            v.SabotageAnytime = r.ReadBoolean();
            v.MaxPurchases = r.ReadByte();
            v.Purchases.Clear();
            int purchases = r.ReadByte();
            for (int i = 0; i < purchases; i++)
                v.Purchases.Add(new PurchaseView { Buyer = r.ReadByte(), Target = r.ReadByte(), Def = r.ReadInt16(), Tier = r.ReadByte() });
            v.MatchWinner = r.ReadSByte();
            ReadInts(r, v.Ranking);
            v.WinnerReward = r.ReadInt16();
            v.ParticipationReward = r.ReadInt16();
            v.CoinCap = r.ReadInt16();

            int players = r.ReadByte();
            while (v.Players.Count < players) v.Players.Add(new PlayerView());
            while (v.Players.Count > players) v.Players.RemoveAt(v.Players.Count - 1);
            foreach (var p in v.Players)
            {
                p.Id = r.ReadByte();
                p.SlotId = r.ReadByte();
                p.ClientId = r.ReadUInt64();
                p.Connected = r.ReadBoolean();
                ReadProfile(r, p.Profile);
                p.Lives = r.ReadByte();
                p.Eliminated = r.ReadBoolean();
                p.EliminatedRound = r.ReadSByte();
                p.Coins = r.ReadInt16();
                p.RoundWins = r.ReadByte();
                p.LegalPockets = r.ReadInt16();
                p.Fouls = r.ReadInt16();
                p.SabotagesDone = r.ReadByte();
                p.SabotagesReceived = r.ReadByte();
                p.CoinsEarned = r.ReadInt16();
                p.CoinsSpent = r.ReadInt16();
                p.Immune = r.ReadBoolean();
                p.ShopPassed = r.ReadBoolean();
                p.PurchasesThisWindow = r.ReadByte();
            p.SabotageCooldown = r.ReadByte() / 4f;
            p.ImmuneFor = r.ReadByte() / 4f;
                p.Mods.Clear();
                int mods = r.ReadByte();
                for (int i = 0; i < mods; i++)
                {
                    p.Mods.Add(new ModView
                    {
                        Def = r.ReadInt16(),
                        Remaining = r.ReadByte(),
                        Intensity = r.ReadByte(),
                        Source = r.ReadSByte(),
                        DurationType = (ModifierDurationType)r.ReadByte()
                    });
                }
            }

            for (int tier = 0; tier < MatchView.Tiers; tier++)
                for (int b = 0; b < players; b++)
                    for (int t = 0; t < players; t++)
                    {
                        int i = MatchView.ShopIndex(tier, b, t);
                        v.ShopBlock[i] = (SabotageBlock)r.ReadByte();
                        v.ShopCost[i] = r.ReadInt16();
                    }

            var tel = v.Telemetry;
            tel.Shots = r.ReadInt16();
            tel.Fouls = r.ReadInt16();
            tel.Timeouts = r.ReadInt16();
            tel.RiskEvents = r.ReadInt16();
            tel.Discharges = r.ReadInt16();
            tel.Sabotages = r.ReadInt16();
            tel.Duration = r.ReadSingle();
            for (int i = 0; i < 3; i++) tel.Buckets[i] = r.ReadInt16();
            tel.ModifierCounts.Clear();
            int modCounts = r.ReadByte();
            for (int i = 0; i < modCounts; i++) tel.ModifierCounts.Add(new KeyValuePair<int, int>(r.ReadInt16(), r.ReadInt16()));

            ReadBalls(r, v.Balls);
        }
    }

    /// <summary>Informação de cada lugar da partida que só a camada de rede conhece.</summary>
    public struct SeatInfo
    {
        public int SlotId;
        public ulong ClientId;
        public bool Connected;
    }

    /// <summary>Constrói o MatchView a partir do estado autoritativo (só corre no servidor/host).</summary>
    public static class MatchViewBuilder
    {
        public static void CaptureMatch(MatchDirector d, IReadOnlyList<ModifierDefinition> catalog, IReadOnlyList<SeatInfo> seats, MatchView v)
        {
            v.InMatch = true;
            v.Phase = d.Phase;
            v.PhaseDuration = d.PhaseDuration;
            v.PhaseRemaining = d.PhaseTimeRemaining;
            v.RoundIndex = d.RoundIndex;
            v.RoundLimit = d.Config.RoundLimit;
            v.SuddenDeath = d.IsSuddenDeath;
            v.CurrentPlayer = d.CurrentPlayerId;
            v.BallInHand = d.BallInHand;
            v.KitchenOnly = d.BallInHandKitchenOnly;
            v.ShotSequence = d.ShotSequence;
            v.FinalBall = d.Sim.FinalBall;
            Copy(d.RoundOrder, v.RoundOrder);
            Copy(d.BaseOrder, v.BaseOrder);
            v.RoundWinner = d.RoundWinnerId;
            Copy(d.PunishmentCandidates, v.Candidates);
            v.Selected = d.SelectedPlayerId;
            v.RiskEventId = d.RiskEventId ?? "";
            v.HasRiskResult = d.HasRiskResult;
            v.RiskOutcome = d.LastRiskOutcome;
            v.RiskModifierDef = d.LastRiskModifier != null ? IndexOf(catalog, d.LastRiskModifier.Definition) : -1;
            v.RiskEliminated = d.LastRiskEliminated;
            v.RiskAutomatic = d.LastRiskWasAutomatic;
            v.DryStreak = d.RiskBag.DryStreak;
            v.LoadedChambers = d.LoadedChambers;
            v.Chambers = d.RiskBag.TotalTokens;
            v.ShopWindow = d.ShopWindowIndex;
            v.ShopOpen = d.Sabotage.IsOpen;
            v.SabotageAnytime = d.SabotageContinuous;
            v.MaxPurchases = d.Config.MaxPurchasesPerWindow;
            v.Purchases.Clear();
            foreach (var p in d.ShopPurchases)
                v.Purchases.Add(new PurchaseView { Buyer = p.BuyerId, Target = p.TargetId, Def = IndexOf(catalog, p.Applied.Definition), Tier = p.Tier });
            v.MatchWinner = d.MatchWinnerId;
            Copy(d.FinalRanking, v.Ranking);
            v.WinnerReward = d.Config.WinnerReward;
            v.ParticipationReward = d.Config.ParticipationReward;
            v.CoinCap = d.Config.CoinCap;

            while (v.Players.Count < d.Players.Count) v.Players.Add(new PlayerView());
            while (v.Players.Count > d.Players.Count) v.Players.RemoveAt(v.Players.Count - 1);
            for (int i = 0; i < d.Players.Count; i++)
            {
                var s = d.Players[i];
                var p = v.Players[i];
                var seat = i < seats.Count ? seats[i] : new SeatInfo { SlotId = i, Connected = true };
                p.Id = s.Id;
                p.SlotId = seat.SlotId;
                p.ClientId = seat.ClientId;
                p.Connected = seat.Connected || s.Profile.IsBot;
                p.Profile = s.Profile;
                p.Lives = s.Lives;
                p.Eliminated = s.Eliminated;
                p.EliminatedRound = s.EliminatedRound;
                p.Coins = s.Coins;
                p.RoundWins = s.RoundWins;
                p.LegalPockets = s.LegalPockets;
                p.Fouls = s.Fouls;
                p.SabotagesDone = s.SabotagesDone;
                p.SabotagesReceived = s.SabotagesReceived;
                p.CoinsEarned = s.CoinsEarned;
                p.CoinsSpent = s.CoinsSpent;
                p.ImmuneFor = d.SabotageContinuous ? d.Sabotage.ImmunityFor(s.Id) : 0f;
                p.SabotageCooldown = d.SabotageContinuous ? d.Sabotage.CooldownFor(s.Id) : 0f;
                p.Immune = d.SabotageContinuous ? p.ImmuneFor > 0f : s.IsImmune(d.Sabotage.IsOpen ? d.ShopWindowIndex : d.ShopWindowIndex + 1);
                p.ShopPassed = d.HasShopPassed(s.Id);
                p.PurchasesThisWindow = d.Sabotage.PurchasesThisWindow(s.Id);
                p.Mods.Clear();
                foreach (var m in s.Modifiers.Active)
                {
                    p.Mods.Add(new ModView
                    {
                        Def = IndexOf(catalog, m.Definition),
                        Remaining = m.RemainingDuration,
                        Intensity = m.Intensity,
                        Source = m.SourcePlayerId,
                        DurationType = m.Definition.DurationType
                    });
                }
            }

            for (int tier = 0; tier < MatchView.Tiers; tier++)
                for (int b = 0; b < d.Players.Count; b++)
                    for (int t = 0; t < d.Players.Count; t++)
                    {
                        var buyer = d.Players[b];
                        var target = d.Players[t];
                        int i = MatchView.ShopIndex(tier, b, t);
                        v.ShopBlock[i] = d.Sabotage.IsOpen ? d.Sabotage.ValidateTargetCode(buyer, target, tier) : SabotageBlock.ShopClosed;
                        v.ShopCost[i] = d.Sabotage.CostFor(buyer, target, tier);
                    }

            var tel = d.Telemetry;
            v.Telemetry.Shots = tel.Shots;
            v.Telemetry.Fouls = tel.Fouls;
            v.Telemetry.Timeouts = tel.Timeouts;
            v.Telemetry.RiskEvents = tel.RiskEvents;
            v.Telemetry.Discharges = tel.Discharges;
            v.Telemetry.Sabotages = tel.SabotageSuccesses;
            v.Telemetry.Duration = tel.MatchDuration;
            for (int i = 0; i < 3; i++) v.Telemetry.Buckets[i] = tel.ShotsByModifierCount[i];
            v.Telemetry.ModifierCounts.Clear();
            foreach (var kv in tel.ModifierApplications)
            {
                int index = -1;
                for (int i = 0; i < catalog.Count; i++)
                    if (catalog[i].Id == kv.Key) index = i;
                v.Telemetry.ModifierCounts.Add(new KeyValuePair<int, int>(index, kv.Value));
            }

            d.Sim.CopyState(v.Balls);
        }

        static void Copy(IReadOnlyList<int> source, List<int> target)
        {
            target.Clear();
            for (int i = 0; i < source.Count; i++) target.Add(source[i]);
        }

        public static int IndexOf(IReadOnlyList<ModifierDefinition> catalog, ModifierDefinition def)
        {
            for (int i = 0; i < catalog.Count; i++)
                if (catalog[i] == def || (def != null && catalog[i].Id == def.Id)) return i;
            return -1;
        }
    }
}
