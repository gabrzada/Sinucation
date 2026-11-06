using System.Collections.Generic;
using System.Linq;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Net;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class GameplayGoalTests
    {
        [TestCase(null, true)]
        [TestCase("", true)]
        [TestCase("[Disconnect Event][Client-0][TransportClientId-0][TransportShutdown] NetworkConnectionManager was shut down.", true)]
        [TestCase("[Disconnect Event][Client-1][ProtocolTimeout] Connection timed out.", true)]
        [TestCase("Foste expulso pelo anfitrião.", false)]
        [TestCase("Versão do jogo diferente da do anfitrião.", false)]
        public void TransportDiagnosticsAllowRecoveryButExplicitRejectionDoesNot(string reason, bool expected)
        {
            Assert.AreEqual(expected, SessionService.HasTransientDisconnectReason(reason));
        }

        [TestCase(0)] [TestCase(1)]
        public void PurchaseWithExactQuotedBalanceAppliesOnceAndReplicates(int tier)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            config.SabotageAnytime = true;
            config.RoundIntroSeconds = 0.01f;
            var catalog = ModifierCatalog.CreateDefaults();
            try
            {
                var profiles = new[] { new PlayerProfile("Buyer", Archetype.Circle, Color.white), new PlayerProfile("Target", Archetype.Square, Color.red) };
                var d = new MatchDirector(config, catalog, profiles, 42);
                d.Start();
                d.Tick(0.1f);
                var buyer = d.GetPlayer(0);
                var target = d.GetPlayer(1);
                // Exercise repeat-target and comeback modifiers together, not merely the advertised base price.
                buyer.LastTargetId = 1;
                target.RoundWins = config.UnderdogGap;
                int quote = d.Sabotage.CostFor(buyer, target, tier);
                buyer.Coins = quote;
                var request = new PurchaseRequest { ClientRequestId = "goal-purchase", BuyerId = 0, TargetId = 1, Tier = tier };
                var result = d.SubmitPurchase(request);
                Assert.IsTrue(result.Accepted, result.Reason);
                Assert.AreEqual(quote, result.Cost);
                Assert.AreEqual(0, buyer.Coins);
                Assert.AreEqual(tier, (int)result.Applied.Definition.Tier);
                Assert.AreEqual(3, result.Applied.RemainingDuration);
                Assert.AreEqual(PurchaseStatus.Duplicate, d.SubmitPurchase(request).Status);
                Assert.AreEqual(0, buyer.Coins);
                Assert.AreEqual(1, target.Modifiers.Active.Count);
                Assert.AreEqual(1, d.Telemetry.SabotageSuccesses);
                var authoritative = new MatchView();
                MatchViewBuilder.CaptureMatch(d, catalog, new List<SeatInfo>(), authoritative);
                var client = new MatchView();
                MatchViewCodec.Deserialize(MatchViewCodec.Serialize(authoritative), client);
                Assert.AreEqual(0, client.GetPlayer(0).Coins);
                var replicated = client.GetPlayer(1).Mods.Single();
                Assert.AreEqual(result.Applied.Definition.Id, catalog[replicated.Def].Id);
                Assert.AreEqual(3, replicated.Remaining);
            }
            finally
            {
                Object.DestroyImmediate(config);
                foreach (var def in catalog) Object.DestroyImmediate(def);
            }
        }

        [TestCase(ModifierCatalog.Myopia)] [TestCase(ModifierCatalog.ColorBlindness)] [TestCase(ModifierCatalog.TunnelVision)]
        public void PerceptionEffectsStackWithCameraRollAndExpireAfterThreeShots(string id)
        {
            // Debuffs acumulativos (2026-10-24): o efeito de percepção junta-se ao "Invertido" em vez de o substituir.
            var catalog = ModifierCatalog.CreateDefaults();
            try
            {
                var controller = new ModifierController();
                controller.Apply(catalog.First(d => d.Id == ModifierCatalog.UpsideDown), 0, 1, "old", 1, 1, 4);
                var effect = controller.Apply(catalog.First(d => d.Id == id), 0, 1, "new", 1, 2, 4);
                Assert.NotNull(effect);
                Assert.AreEqual(2, controller.Active.Count);
                var profile = CameraModifierPipeline.Build(controller.Active, true);
                Assert.IsFalse(profile.IsNeutral);
                Assert.AreEqual(180f, profile.Roll, "o invertido continua");
                if (id == ModifierCatalog.Myopia) Assert.Greater(profile.Myopia, 0);
                if (id == ModifierCatalog.ColorBlindness) Assert.Greater(profile.Deuteranopia, 0);
                if (id == ModifierCatalog.TunnelVision) Assert.Greater(profile.Vignette, 0);
                for (int i = 0; i < 2; i++) { controller.Tick(ModifierDurationType.Shots); Assert.AreEqual(2, controller.Active.Count); }
                controller.Tick(ModifierDurationType.Shots);
                Assert.IsTrue(CameraModifierPipeline.Build(controller.Active, false).IsNeutral);
            }
            finally { foreach (var def in catalog) Object.DestroyImmediate(def); }
        }

        [TestCase(0.25f)] [TestCase(0.65f)] [TestCase(1f)]
        public void AllSixPocketMouthsCaptureShotsAtDifferentSpeeds(float power)
        {
            for (int index = 0; index < 6; index++)
            {
                var sim = new BilliardsSimulation(new BilliardsPhysicsSettings());
                Vector2 pocket = sim.Table.Pockets[index].Center;
                Vector2 direction = pocket.normalized;
                sim.LoadState(new List<BallState> { new BallState { Number = 0, Position = pocket - direction * 0.25f } }, 9);
                var record = new ShotRecord();
                sim.StrikeCue(ShotParameters.FromInput(direction, power, 0, 0), record);
                for (int step = 0; step < 2000 && sim.IsSimulating; step++) sim.Advance(1f / 60f);
                Assert.IsTrue(sim.Cue.Pocketed, "pocket " + index + " power " + power);
                Assert.AreEqual(index, sim.Cue.PocketIndex);
                Assert.AreEqual(0, sim.PhysicsRecoveries);
            }
        }

        [Test]
        public void SolidCushionsAndArbitraryOutOfBoundsAreNotPockets()
        {
            var sim = new BilliardsSimulation(new BilliardsPhysicsSettings());
            float hw = sim.Table.HalfWidth, hl = sim.Table.HalfLength;
            foreach (float sign in new[] { -1f, 1f })
            {
                Assert.AreEqual(-1, sim.PocketAt(new Vector2(0.5f, sign * (hw - 0.03f))));
                Assert.AreEqual(-1, sim.PocketAt(new Vector2(sign * (hl - 0.03f), 0.25f)));
                Assert.AreEqual(-1, sim.PocketAt(new Vector2(0.5f, sign * (hw + 0.02f))));
                Assert.AreEqual(-1, sim.PocketAt(new Vector2(sign * 10f, 10f)));
            }
        }

        [Test]
        public void TableBarrierOnlyBlocksPlayersAndPropsCannotHitBilliards()
        {
            GameplayCollision.Configure();
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(GameplayCollision.Player, GameplayCollision.TableBarrier));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(GameplayCollision.Throwable, GameplayCollision.TableBarrier));
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(GameplayCollision.Throwable, GameplayCollision.BilliardBall));
            Assert.AreEqual(0, GameplayCollision.WorldQueryMask & (1 << GameplayCollision.TableBarrier), "Invisible barrier cannot occlude music, pings, guns or spectator camera.");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(GameplayCollision.Throwable, 0), "Props still collide with the physical table.");
        }
    }
}
