using System;
using System.Collections.Generic;
using System.Linq;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Pausa de quem joga sozinho contra bots: com a partida pausada nada avança (fases, relógio do turno, física, roleta,
    /// loja, recargas da sabotagem, bots) e os comandos são recusados; ao retomar tudo continua exactamente de onde parou.
    /// A pausa só existe com um humano e, durante ela, ninguém entra.
    /// </summary>
    public class SoloPauseTests
    {
        readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in _owned)
                if (o != null) Object.DestroyImmediate(o);
            _owned.Clear();
        }

        MatchDirector NewMatch(int players, int seed, Action<MatchConfig> configure = null, bool bots = true)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            configure?.Invoke(config);
            var catalog = ModifierCatalog.CreateDefaults();
            _owned.Add(config);
            _owned.AddRange(catalog);
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < players; i++) profiles.Add(new PlayerProfile("P" + i, (Archetype)(i % 3), Color.white) { IsBot = bots });
            return new MatchDirector(config, catalog, profiles, seed);
        }

        static void Drive(MatchDirector d, BotBrain brain, Func<bool> until, float dt = 0.05f, int maxTicks = 400000)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (until()) return;
                Assert.AreNotEqual(MatchPhase.MatchOver, d.Phase, "a partida acabou antes de chegar ao cenário");
                brain?.Update(d, dt);
                d.Tick(dt);
            }
            Assert.Fail("cenário não alcançado; fase " + d.Phase);
        }

        static void TickPaused(MatchDirector d, BotBrain brain, float seconds, float dt = 0.05f)
        {
            for (float t = 0f; t < seconds; t += dt)
            {
                brain?.Update(d, dt);
                d.Tick(dt);
            }
        }

        static List<BallState> Balls(MatchDirector d)
        {
            var list = new List<BallState>();
            d.Sim.CopyState(list);
            return list;
        }

        static ShotRequest AimAtLowest(MatchDirector d, float power) => new ShotRequest
        {
            PlayerId = d.CurrentPlayerId,
            ShotSequence = d.ShotSequence,
            Direction = d.Sim.GetBall(d.Sim.LowestBallOnTable()).Position - d.Sim.Cue.Position,
            Power = power
        };

        [Test]
        public void RoundIntroClockAndMatchTimeStopWhilePausedAndResumeFromTheSameValue()
        {
            var d = NewMatch(3, 5, c => c.RoundIntroSeconds = 2f, bots: false);
            d.Start();
            d.Tick(0.5f);
            Assert.AreEqual(MatchPhase.RoundIntro, d.Phase);
            float remaining = d.PhaseTimeRemaining, time = d.MatchTime;

            Assert.IsTrue(d.SetPaused(true, "teste"));
            Assert.IsFalse(d.SetPaused(true, "teste"), "pausar duas vezes não faz nada");
            TickPaused(d, null, 30f);
            Assert.AreEqual(MatchPhase.RoundIntro, d.Phase);
            Assert.AreEqual(remaining, d.PhaseTimeRemaining);
            Assert.AreEqual(time, d.MatchTime);

            Assert.IsTrue(d.SetPaused(false, "teste"));
            d.Tick(remaining - 0.05f);
            Assert.AreEqual(MatchPhase.RoundIntro, d.Phase, "retoma com o tempo que faltava, não reinicia nem salta");
            d.Tick(0.1f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            Assert.That(d.Log.ToJsonLines(), Does.Contain("\"pause\"").And.Contain("\"resume\""));
        }

        [Test]
        public void TurnTimerFreezesAndShotsAndBallInHandAreRefusedWhilePaused()
        {
            var d = NewMatch(2, 9, c => { c.RoundIntroSeconds = 0.1f; c.TurnTimeLimit = 20f; }, bots: false);
            d.Start();
            d.Tick(0.2f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            int shooter = d.CurrentPlayerId;
            d.Tick(5f);
            Assert.AreEqual(15f, d.PhaseTimeRemaining, 1e-3f);

            d.SetPaused(true, "teste");
            TickPaused(d, null, 120f);
            Assert.AreEqual(15f, d.PhaseTimeRemaining, 1e-3f, "o relógio do turno não corre durante a pausa");
            Assert.AreEqual(shooter, d.CurrentPlayerId);
            int sequence = d.ShotSequence;
            Assert.AreEqual(CommandResult.Rejected, d.SubmitShot(AimAtLowest(d, 0.5f)));
            Assert.AreEqual(sequence, d.ShotSequence);
            Assert.IsTrue(d.BallInHand);
            Assert.AreEqual("A partida está em pausa.", d.PlaceCueBall(shooter, d.Sim.Cue.Position));

            d.SetPaused(false, "teste");
            d.Tick(14.9f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            Assert.AreEqual(shooter, d.CurrentPlayerId, "o turno continua com os 15 s que faltavam");
            d.Tick(0.2f);
            Assert.AreNotEqual(shooter, d.CurrentPlayerId, "e só depois deles acaba o tempo");
            Assert.AreEqual(1, d.Telemetry.Timeouts);
        }

        [Test]
        public void RollingBallsStopExactlyWhilePausedAndKeepRollingAfterResume()
        {
            var d = NewMatch(2, 21, c => c.RoundIntroSeconds = 0.1f, bots: false);
            d.Start();
            d.Tick(0.2f);
            Assert.AreEqual(CommandResult.Accepted, d.SubmitShot(AimAtLowest(d, 0.95f)));
            d.Tick(MatchDirector.ShotContactSeconds + 0.01f);
            for (int i = 0; i < 6; i++) d.Tick(1f / 60f);
            Assert.AreEqual(MatchPhase.Simulating, d.Phase);
            Assert.IsTrue(d.Sim.IsSimulating);

            d.SetPaused(true, "teste");
            var frozen = Balls(d);
            TickPaused(d, null, 10f, 1f / 60f);
            var after = Balls(d);
            Assert.AreEqual(MatchPhase.Simulating, d.Phase);
            Assert.IsTrue(d.Sim.IsSimulating);
            for (int i = 0; i < frozen.Count; i++)
                Assert.AreEqual(frozen[i].Position, after[i].Position, "bola " + frozen[i].Number + " mexeu-se durante a pausa");

            d.SetPaused(false, "teste");
            d.Tick(1f / 60f);
            Assert.IsTrue(Balls(d).Where((b, i) => b.Position != frozen[i].Position).Any(), "as bolas voltam a rolar");
            for (int i = 0; i < 20000 && d.Phase == MatchPhase.Simulating; i++) d.Tick(1f / 60f);
            Assert.AreEqual(MatchPhase.ShotResult, d.Phase, "a tacada resolve-se normalmente depois de retomar");
        }

        [Test]
        public void RevolverNeitherFiresNorTimesOutWhilePausedAndTheBotPullsAfterResume()
        {
            var d = NewMatch(3, 31, c => { c.RoundIntroSeconds = 0.1f; c.FixedBallCount = 3; c.RiskDangerTokens = 0; });
            var brain = new BotBrain(31);
            d.Start();
            Drive(d, brain, () => d.Phase == MatchPhase.AwaitTrigger);
            d.Tick(0.5f);
            string risk = d.RiskEventId;
            int selected = d.SelectedPlayerId;
            float remaining = d.PhaseTimeRemaining;

            d.SetPaused(true, "teste");
            TickPaused(d, brain, d.Config.TriggerTimeout * 4f);
            Assert.AreEqual(MatchPhase.AwaitTrigger, d.Phase, "nem o bot dispara nem o tempo do gatilho acaba");
            Assert.IsFalse(d.HasRiskResult);
            Assert.AreEqual(remaining, d.PhaseTimeRemaining);
            Assert.AreEqual(CommandResult.Rejected, d.PullTrigger(selected, risk), "o gatilho não responde durante a pausa");

            d.SetPaused(false, "teste");
            Drive(d, brain, () => d.Phase != MatchPhase.AwaitTrigger);
            Assert.AreEqual(MatchPhase.RiskReveal, d.Phase);
            Assert.IsFalse(d.LastRiskWasAutomatic, "o bot puxa o gatilho, não é o tempo que esgota");
        }

        [Test]
        public void ShopWindowTimerPurchasesAndPassesFreezeWhilePaused()
        {
            var d = NewMatch(3, 41, c => { c.RoundIntroSeconds = 0.1f; c.FixedBallCount = 3; c.RiskDangerTokens = 0; c.SabotageAnytime = false; c.ShopWindowSeconds = 20f; });
            var brain = new BotBrain(41);
            d.Start();
            Drive(d, brain, () => d.Phase == MatchPhase.Shop);
            float remaining = d.PhaseTimeRemaining;
            int window = d.ShopWindowIndex;

            d.SetPaused(true, "teste");
            TickPaused(d, brain, 60f);
            Assert.AreEqual(MatchPhase.Shop, d.Phase);
            Assert.AreEqual(remaining, d.PhaseTimeRemaining);
            var buyer = d.Players.First(p => p.IsActive);
            var target = d.Players.First(p => p.IsActive && p.Id != buyer.Id);
            buyer.Coins = 500;
            var result = d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "pausa", BuyerId = buyer.Id, TargetId = target.Id, Tier = 0 });
            Assert.AreEqual(PurchaseStatus.Rejected, result.Status);
            Assert.AreEqual("Partida em pausa", result.Reason);
            Assert.AreEqual(500, buyer.Coins);
            foreach (var p in d.Players) d.PassShop(p.Id);
            Assert.AreEqual(MatchPhase.Shop, d.Phase, "passar a loja não a fecha durante a pausa");

            d.SetPaused(false, "teste");
            Assert.AreEqual(PurchaseStatus.Accepted, d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "depois", BuyerId = buyer.Id, TargetId = target.Id, Tier = 0 }).Status);
            Assert.AreEqual(window, d.ShopWindowIndex);
        }

        [Test]
        public void SabotageCooldownsUseTheMatchClockSoTheyStopWhilePaused()
        {
            var d = NewMatch(3, 51, c => { c.RoundIntroSeconds = 0.1f; c.SabotageCooldownSeconds = 20f; }, bots: false);
            d.Start();
            d.Tick(0.2f);
            Assert.IsTrue(d.SabotageContinuous);
            d.GetPlayer(0).Coins = 500;
            Assert.AreEqual(PurchaseStatus.Accepted, d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "a", BuyerId = 0, TargetId = 1, Tier = 0 }).Status);
            d.Tick(4f);
            float cooldown = d.Sabotage.CooldownFor(0);
            Assert.AreEqual(16f, cooldown, 0.01f);

            d.SetPaused(true, "teste");
            TickPaused(d, null, 60f);
            Assert.AreEqual(cooldown, d.Sabotage.CooldownFor(0), "a recarga não desce durante a pausa");

            d.SetPaused(false, "teste");
            d.Tick(3f);
            Assert.AreEqual(cooldown - 3f, d.Sabotage.CooldownFor(0), 0.01f);
        }

        [Test]
        public void BotsKeepTheirAimAndNeverShootWhilePaused()
        {
            var d = NewMatch(2, 61, c => c.RoundIntroSeconds = 0.1f);
            var brain = new BotBrain(61);
            d.Start();
            Drive(d, brain, () => d.Phase == MatchPhase.AwaitShot && brain.Aim.IsValid, 1f / 60f);
            var aim = brain.Aim;
            int sequence = d.ShotSequence;
            float remaining = d.PhaseTimeRemaining;

            d.SetPaused(true, "teste");
            TickPaused(d, brain, 60f, 1f / 60f);
            Assert.AreEqual(sequence, d.ShotSequence, "o bot não taca durante a pausa");
            Assert.AreEqual(remaining, d.PhaseTimeRemaining);
            Assert.AreEqual(aim.PlayerId, brain.Aim.PlayerId);
            Assert.AreEqual(aim.Yaw, brain.Aim.Yaw, "a mira fica onde estava (o taco não salta)");
            Assert.AreEqual(aim.Pull, brain.Aim.Pull);

            d.SetPaused(false, "teste");
            Drive(d, brain, () => d.ShotSequence != sequence, 1f / 60f, 60 * 25);
            Assert.Less(d.Telemetry.Timeouts, 1, "o bot taca a tempo depois de retomar");
        }

        [Test]
        public void PostMatchFunModeCanBePausedButAMatchThatNeverStartedCannot()
        {
            var idle = NewMatch(2, 71, bots: false);
            Assert.IsFalse(idle.SetPaused(true, "teste"), "antes de começar não há partida para pausar");

            var d = NewMatch(3, 71, c => c.RoundIntroSeconds = 0.1f, bots: false);
            d.Start();
            d.Tick(0.2f);
            d.Forfeit(0, "teste");
            d.Forfeit(1, "teste");
            Assert.AreEqual(MatchPhase.MatchOver, d.Phase);
            Assert.IsTrue(d.SetPaused(true, "modo livre"));
            d.Tick(5f);
            Assert.AreEqual(MatchPhase.MatchOver, d.Phase);
            Assert.IsTrue(d.SetPaused(false, "modo livre"));
        }

        [Test]
        public void SnapshotCarriesThePauseSoClientClocksStopToo()
        {
            var d = NewMatch(2, 81, c => c.RoundIntroSeconds = 0.1f, bots: false);
            d.Start();
            d.Tick(0.2f);
            d.SetPaused(true, "teste");
            var source = new MatchView();
            MatchViewBuilder.CaptureMatch(d, new List<ModifierDefinition>(), new List<SeatInfo>(), source);
            source.PauseAllowed = true;
            var copy = new MatchView();
            MatchViewCodec.Deserialize(MatchViewCodec.Serialize(source), copy);
            Assert.IsTrue(copy.MatchPaused);
            Assert.IsTrue(copy.PauseAllowed);
            Assert.AreEqual(source.PhaseRemaining, copy.PhaseRemaining);

            d.SetPaused(false, "teste");
            MatchViewBuilder.CaptureMatch(d, new List<ModifierDefinition>(), new List<SeatInfo>(), source);
            source.PauseAllowed = false;
            MatchViewCodec.Deserialize(MatchViewCodec.Serialize(source), copy);
            Assert.IsFalse(copy.MatchPaused);
            Assert.IsFalse(copy.PauseAllowed);
            Assert.GreaterOrEqual(NetGame.ProtocolVersion, 13, "o snapshot mudou: clientes antigos não podem entrar");
        }

        [TestCase(true, 1, true, 0, ExpectedResult = true, TestName = "Sozinho contra bots: pode pausar")]
        [TestCase(true, 2, true, 0, ExpectedResult = false, TestName = "Espectador humano ligado: sem pausa")]
        [TestCase(true, 2, true, 1, ExpectedResult = false, TestName = "Dois humanos sentados: sem pausa")]
        [TestCase(true, 1, true, 1, ExpectedResult = false, TestName = "Humano desligado com lugar reservado: sem pausa")]
        [TestCase(true, 1, false, 0, ExpectedResult = false, TestName = "Anfitrião sem lugar na partida: sem pausa")]
        [TestCase(false, 1, true, 0, ExpectedResult = false, TestName = "Sala de espera: sem pausa")]
        public bool PauseOnlyExistsForASingleHumanAgainstBots(bool inMatch, int connected, bool hostSeated, int otherHumans) =>
            SoloPause.Allowed(inMatch, connected, hostSeated, otherHumans);

        [Test]
        public void JoinsAreRefusedWithAClearReasonOnlyWhilePaused()
        {
            Assert.IsTrue(SoloPause.RefusesJoin(true));
            Assert.IsFalse(SoloPause.RefusesJoin(false));
            Assert.That(SoloPause.JoinRefusedReason, Does.Contain("pausa"));
        }
    }
}
