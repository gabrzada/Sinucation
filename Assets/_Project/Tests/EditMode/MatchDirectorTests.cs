using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Joga partidas completas sem rendering com "bots" que enviam os mesmos comandos que o input
    /// real. Verifica o caminho crítico: tacada → ronda → sorteio → gatilho → sabotagem → fim.
    /// </summary>
    public class MatchDirectorTests
    {
        static MatchDirector NewMatch(int players, int seed, System.Action<MatchConfig> configure = null)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            configure?.Invoke(config);
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < players; i++) profiles.Add(new PlayerProfile("P" + i, (Archetype)(i % 3), Color.white));
            return new MatchDirector(config, ModifierCatalog.CreateDefaults(), profiles, seed);
        }

        sealed class Observations
        {
            public int RoundsWon;
            public int PunishmentsChecked;
            public int Purchases;
            public int MinCoins = int.MaxValue;
            public readonly HashSet<MatchPhase> Phases = new HashSet<MatchPhase>();
        }

        static Observations Play(MatchDirector d, int seed)
        {
            var obs = new Observations();
            var rng = new System.Random(seed);
            int requestCounter = 0;
            d.RoundWon += id => obs.RoundsWon++;
            d.PhaseChanged += phase =>
            {
                obs.Phases.Add(phase);
                if (phase == MatchPhase.PunishmentDraw)
                {
                    Assert.AreNotEqual(d.RoundWinnerId, d.SelectedPlayerId, "o vencedor nunca pode ser sorteado");
                    Assert.IsTrue(d.GetPlayer(d.SelectedPlayerId).IsActive);
                    obs.PunishmentsChecked++;
                }
            };

            d.Start();
            for (int tick = 0; tick < 600000 && d.Phase != MatchPhase.MatchOver; tick++)
            {
                switch (d.Phase)
                {
                    case MatchPhase.AwaitShot:
                    {
                        var target = d.Sim.GetBall(d.Sim.LowestBallOnTable());
                        Vector2 dir = target.Position - d.Sim.Cue.Position;
                        float jitter = (float)(rng.NextDouble() - 0.5) * 6f * Mathf.Deg2Rad;
                        dir = new Vector2(dir.x * Mathf.Cos(jitter) - dir.y * Mathf.Sin(jitter), dir.x * Mathf.Sin(jitter) + dir.y * Mathf.Cos(jitter));
                        var result = d.SubmitShot(new ShotRequest
                        {
                            PlayerId = d.CurrentPlayerId,
                            ShotSequence = d.ShotSequence,
                            Direction = dir,
                            Power = 0.35f + (float)rng.NextDouble() * 0.55f
                        });
                        Assert.AreEqual(CommandResult.Accepted, result);
                        break;
                    }
                    case MatchPhase.AwaitTrigger:
                        // O revólver tem de chegar à cabeça antes de o gatilho responder (igual em todos os ecrãs).
                        if (d.PhaseElapsed < d.Config.TriggerArmSeconds)
                            Assert.AreEqual(CommandResult.Rejected, d.PullTrigger(d.SelectedPlayerId, d.RiskEventId));
                        else if (rng.NextDouble() < 0.8) Assert.AreEqual(CommandResult.Accepted, d.PullTrigger(d.SelectedPlayerId, d.RiskEventId));
                        break;
                    case MatchPhase.Shop:
                        foreach (var buyer in d.Players)
                        {
                            if (!buyer.IsActive || d.HasShopPassed(buyer.Id) || d.Phase != MatchPhase.Shop) continue;
                            if (rng.NextDouble() < 0.4)
                            {
                                d.PassShop(buyer.Id);
                                continue;
                            }
                            var targetPlayer = d.Players[rng.Next(d.Players.Count)];
                            var r = d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "bot-" + (++requestCounter), BuyerId = buyer.Id, TargetId = targetPlayer.Id });
                            if (r.Accepted) obs.Purchases++;
                        }
                        break;
                }

                d.Tick(0.1f);
                foreach (var p in d.Players) obs.MinCoins = Mathf.Min(obs.MinCoins, p.Coins);
            }
            return obs;
        }

        [TestCase(2, 5)]
        [TestCase(3, 11)]
        [TestCase(4, 22)]
        [TestCase(5, 33)]
        [TestCase(6, 44)]
        public void FullMatch_ReachesMatchOverWithConsistentState(int players, int seed)
        {
            var d = NewMatch(players, seed);
            var obs = Play(d, seed);

            Assert.AreEqual(MatchPhase.MatchOver, d.Phase, "a partida devia terminar");
            Assert.GreaterOrEqual(d.MatchWinnerId, 0);
            Assert.IsTrue(d.GetPlayer(d.MatchWinnerId).IsActive, "o vencedor tem de estar vivo");
            Assert.AreEqual(1, d.ActiveCount, "Only the last survivor wins.");
            Assert.AreEqual(players, d.FinalRanking.Count);
            Assert.AreEqual(d.MatchWinnerId, d.FinalRanking[0]);
            Assert.GreaterOrEqual(obs.MinCoins, 0, "saldo nunca pode ficar negativo");
            Assert.Greater(obs.RoundsWon, 0);
            Assert.Greater(obs.PunishmentsChecked, 0);
            foreach (var p in d.Players) Assert.LessOrEqual(p.Coins, d.Config.CoinCap);
            foreach (var p in d.Players)
                if (p.Eliminated) Assert.AreEqual(0, p.Coins, "moedas de eliminados são destruídas");
            Assert.IsTrue(d.Log.Entries.Count > 0);
        }

        [Test]
        public void FullMatch_ChaosRiskEliminatesAndStillFinishes()
        {
            var d = NewMatch(4, 5, c =>
            {
                c.StartingLives = 1;
                c.RiskDangerTokens = 2;
                c.RiskTotalTokens = 3;
                c.RoundLimit = 10;
            });
            Play(d, 5);
            Assert.AreEqual(MatchPhase.MatchOver, d.Phase);
            int eliminated = 0;
            foreach (var p in d.Players)
                if (p.Eliminated) eliminated++;
            Assert.Greater(eliminated, 0);
            Assert.AreEqual(1, d.ActiveCount);
            Assert.IsFalse(d.GetPlayer(d.MatchWinnerId).Eliminated);
        }

        [Test]
        public void Shot_WrongPlayerIsRejectedAndReplayIsDuplicate()
        {
            var d = NewMatch(3, 1, c => c.RoundIntroSeconds = 0.1f);
            d.Start();
            d.Tick(0.2f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);

            int other = (d.CurrentPlayerId + 1) % 3;
            var wrong = new ShotRequest { PlayerId = other, ShotSequence = d.ShotSequence, Direction = Vector2.right, Power = 0.5f };
            Assert.AreEqual(CommandResult.Rejected, d.SubmitShot(wrong));

            var request = new ShotRequest { PlayerId = d.CurrentPlayerId, ShotSequence = d.ShotSequence, Direction = Vector2.right, Power = 0.5f };
            Assert.AreEqual(CommandResult.Accepted, d.SubmitShot(request));
            Assert.AreEqual(CommandResult.Duplicate, d.SubmitShot(request));
            Assert.AreEqual(MatchPhase.ShotWindup, d.Phase);
            Assert.IsFalse(d.Sim.IsSimulating, "Ball stays still until cue contact.");
            d.Tick(MatchDirector.ShotContactSeconds);
            Assert.AreEqual(MatchPhase.Simulating, d.Phase);
        }

        [Test]
        public void Trigger_OnlySelectedPlayerCanPullAndOnlyOnce()
        {
            var d = NewMatch(3, 3);
            d.Start();
            var rng = new System.Random(3);
            for (int i = 0; i < 400000 && d.Phase != MatchPhase.AwaitTrigger && d.Phase != MatchPhase.MatchOver; i++)
            {
                if (d.Phase == MatchPhase.AwaitShot)
                {
                    var target = d.Sim.GetBall(d.Sim.LowestBallOnTable());
                    d.SubmitShot(new ShotRequest
                    {
                        PlayerId = d.CurrentPlayerId,
                        ShotSequence = d.ShotSequence,
                        Direction = target.Position - d.Sim.Cue.Position,
                        Power = 0.4f + (float)rng.NextDouble() * 0.5f
                    });
                }
                d.Tick(0.1f);
            }
            Assert.AreEqual(MatchPhase.AwaitTrigger, d.Phase);

            int wrong = d.SelectedPlayerId == 0 ? 1 : 0;
            Assert.AreEqual(CommandResult.Rejected, d.PullTrigger(wrong, d.RiskEventId));
            Assert.AreEqual(CommandResult.Rejected, d.PullTrigger(d.SelectedPlayerId, "RISK-bogus"));
            string riskId = d.RiskEventId;
            Assert.AreEqual(CommandResult.Rejected, d.PullTrigger(d.SelectedPlayerId, riskId), "ainda a levar o revólver à cabeça");
            while (d.PhaseElapsed < d.Config.TriggerArmSeconds) d.Tick(0.1f);
            Assert.AreEqual(CommandResult.Accepted, d.PullTrigger(d.SelectedPlayerId, riskId));
            Assert.AreEqual(CommandResult.Duplicate, d.PullTrigger(d.SelectedPlayerId, riskId));
            Assert.AreEqual(MatchPhase.RiskReveal, d.Phase);
            Assert.AreEqual(1, d.Telemetry.RiskEvents);
        }

        static MatchDirector NewBotMatch(int players, int seed)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < players; i++) profiles.Add(new PlayerProfile("Bot" + i, (Archetype)(i % 3), Color.white) { IsBot = true });
            return new MatchDirector(config, ModifierCatalog.CreateDefaults(), profiles, seed);
        }

        [TestCase(3, 101)]
        [TestCase(6, 202)]
        public void Bots_PlayAFullMatchThroughTheSameCommands(int players, int seed)
        {
            var d = NewBotMatch(players, seed);
            var brain = new BotBrain(seed);
            int purchases = 0;
            d.SabotageApplied += r => purchases++;
            d.Start();
            for (int i = 0; i < 600000 && d.Phase != MatchPhase.MatchOver; i++)
            {
                brain.Update(d, 0.1f);
                d.Tick(0.1f);
            }
            Assert.AreEqual(MatchPhase.MatchOver, d.Phase);
            Assert.GreaterOrEqual(d.MatchWinnerId, 0);
            Assert.Greater(d.Telemetry.Shots, 0);
            Assert.AreEqual(0, d.Telemetry.Timeouts, "bots nunca deviam deixar o tempo esgotar");
        }

        [Test]
        public void Forfeit_CurrentPlayerPassesTurnAndMatchContinues()
        {
            var d = NewMatch(4, 12, c => c.RoundIntroSeconds = 0.1f);
            d.Start();
            d.Tick(0.2f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            int current = d.CurrentPlayerId;

            d.Forfeit(current, "teste");

            Assert.IsTrue(d.GetPlayer(current).Eliminated);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            Assert.AreNotEqual(current, d.CurrentPlayerId);
            CollectionAssert.DoesNotContain(d.RoundOrder, current);
        }

        [Test]
        public void Forfeit_LeavingOnlyOnePlayerEndsTheMatch()
        {
            var d = NewMatch(3, 13, c => c.RoundIntroSeconds = 0.1f);
            d.Start();
            d.Tick(0.2f);
            d.Forfeit(0, "teste");
            d.Forfeit(1, "teste");
            Assert.AreEqual(MatchPhase.MatchOver, d.Phase);
            Assert.AreEqual(2, d.MatchWinnerId);
        }

        [Test]
        public void MatchView_RoundTripsThroughCodec()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < 4; i++) profiles.Add(new PlayerProfile("P" + i, (Archetype)(i % 3), new Color(0.2f * i, 0.5f, 0.9f)) { IsBot = true, HatId = i % 5, AccessoryId = (i + 1) % 5 });
            var d = new MatchDirector(config, catalog, profiles, 77);
            var brain = new BotBrain(77);
            d.Start();
            for (int i = 0; i < 400000 && d.Phase != MatchPhase.Shop && d.Phase != MatchPhase.MatchOver; i++)
            {
                brain.Update(d, 0.1f);
                d.Tick(0.1f);
            }

            var seats = new List<FriendSlop.Net.SeatInfo>();
            for (int i = 0; i < 4; i++) seats.Add(new FriendSlop.Net.SeatInfo { SlotId = i, ClientId = (ulong)(10 + i), Connected = i != 2 });
            var source = new FriendSlop.Net.MatchView { JoinCode = "ABC123", Kind = FriendSlop.Net.SessionKind.Online };
            FriendSlop.Net.MatchViewBuilder.CaptureMatch(d, catalog, seats, source);

            var copy = new FriendSlop.Net.MatchView();
            FriendSlop.Net.MatchViewCodec.Deserialize(FriendSlop.Net.MatchViewCodec.Serialize(source), copy);

            Assert.AreEqual("ABC123", copy.JoinCode);
            Assert.AreEqual(source.Phase, copy.Phase);
            Assert.AreEqual(source.RoundIndex, copy.RoundIndex);
            Assert.AreEqual(source.CurrentPlayer, copy.CurrentPlayer);
            Assert.AreEqual(source.ShotSequence, copy.ShotSequence);
            CollectionAssert.AreEqual(source.BaseOrder, copy.BaseOrder);
            Assert.AreEqual(source.Players.Count, copy.Players.Count);
            for (int i = 0; i < source.Players.Count; i++)
            {
                var a = source.Players[i];
                var b = copy.Players[i];
                Assert.AreEqual(a.Coins, b.Coins);
                Assert.AreEqual(a.Lives, b.Lives);
                Assert.AreEqual(a.RoundWins, b.RoundWins);
                Assert.AreEqual(a.Connected, b.Connected);
                Assert.AreEqual(a.ClientId, b.ClientId);
                Assert.AreEqual(a.Profile.Name, b.Profile.Name);
                Assert.AreEqual(a.Profile.HatId, b.Profile.HatId);
                Assert.AreEqual(a.Profile.AccessoryId, b.Profile.AccessoryId);
                Assert.AreEqual(a.Mods.Count, b.Mods.Count);
                for (int t = 0; t < source.Players.Count; t++)
                {
                    Assert.AreEqual(source.BlockFor(a.Id, t), copy.BlockFor(a.Id, t));
                    Assert.AreEqual(source.CostFor(a.Id, t), copy.CostFor(a.Id, t));
                }
            }
            Assert.AreEqual(source.Balls.Count, copy.Balls.Count);
            for (int i = 0; i < source.Balls.Count; i++)
            {
                Assert.AreEqual(source.Balls[i].Number, copy.Balls[i].Number);
                Assert.AreEqual(source.Balls[i].Pocketed, copy.Balls[i].Pocketed);
                Assert.AreEqual(source.Balls[i].Position.x, copy.Balls[i].Position.x, 1e-6f);
            }
        }

        [Test]
        public void Rewards_WinnerGetsMoreThanParticipants()
        {
            var d = NewMatch(4, 8);
            int winner = -1;
            var coinsAtWin = new Dictionary<int, int>();
            var pocketsAtWin = new Dictionary<int, int>();
            d.RoundWon += id =>
            {
                if (winner >= 0) return;
                winner = id;
            };
            d.PhaseChanged += phase =>
            {
                if (phase == MatchPhase.RoundWon && coinsAtWin.Count == 0)
                    foreach (var p in d.Players)
                    {
                        coinsAtWin[p.Id] = p.Coins;
                        pocketsAtWin[p.Id] = p.LegalPockets;
                    }
            };
            Play(d, 8);
            Assert.GreaterOrEqual(winner, 0);
            foreach (var kv in coinsAtWin)
                Assert.AreEqual((kv.Key == winner ? d.Config.WinnerReward : d.Config.ParticipationReward) + d.Config.PocketReward * pocketsAtWin[kv.Key], kv.Value,
                    "prémio da ronda + " + d.Config.PocketReward + " por cada bola legal");
        }

        [Test]
        public void Toggles_NoDebuffsMeansNoShopAndCleanSurvival()
        {
            var d = NewMatch(4, 11, c =>
            {
                c.EnableDebuffs = false;
                c.RiskDangerTokens = 0;
            });
            bool shopSeen = false;
            d.PhaseChanged += phase => shopSeen |= phase == MatchPhase.Shop;
            Play(d, 8);
            Assert.IsFalse(shopSeen, "sem debuffs não há loja de sabotagem");
            foreach (var p in d.Players) Assert.AreEqual(0, p.Modifiers.Active.Count);
        }
    }
}
