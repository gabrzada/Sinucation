using System.Collections.Generic;
using System.Linq;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class CoreSystemsTests
    {
        static MatchConfig Config()
        {
            return ScriptableObject.CreateInstance<MatchConfig>();
        }

        static ModifierDefinition Def(List<ModifierDefinition> catalog, string id) => catalog.First(d => d.Id == id);

        // ------------------------------------------------------------------ risk bag

        [Test]
        public void RiskBag_BulletsGrowExponentiallyPerRound()
        {
            var bag = new RiskBag(1, 6, new RandomService(1, null), 1.4f);
            CollectionAssert.AreEqual(new[] { 1, 1, 2, 3, 4, 5, 5, 5 }, Enumerable.Range(1, 8).Select(bag.LoadedFor).ToArray());
            Assert.AreEqual(1, RiskBag.Loaded(1, 6, 1f, 6), "crescimento 1 = sempre uma bala");
            Assert.AreEqual(5, RiskBag.Loaded(1, 6, 3f, 12), "fica sempre pelo menos um tambor de debuff");
        }

        [Test]
        public void RiskBag_DischargeRateFollowsTheLoadedChambers()
        {
            foreach (int round in new[] { 1, 4, 6 })
            {
                var bag = new RiskBag(1, 6, new RandomService(round * 31, null), 1.4f);
                int hits = 0;
                const int draws = 6000;
                for (int i = 0; i < draws; i++)
                    if (bag.Draw(round) == RiskOutcome.Discharge) hits++;
                Assert.AreEqual(bag.ChanceFor(round), hits / (float)draws, 0.03f, "ronda " + round);
            }
        }

        [Test]
        public void RiskBag_WithoutDangerNeverDischarges()
        {
            var bag = new RiskBag(0, 6, new RandomService(7, null), 2f);
            for (int i = 0; i < 50; i++) Assert.AreEqual(RiskOutcome.Debuff, bag.Draw(i + 1));
            Assert.AreEqual(50, bag.DryStreak);
        }

        // ------------------------------------------------------------------ economy

        [Test]
        public void Economy_CreditRespectsCapAndDebitNeverGoesNegative()
        {
            var economy = new EconomyService(250);
            var p = new PlayerState(0, new PlayerProfile("A", Archetype.Circle, Color.red), 2);

            Assert.AreEqual(100, economy.Credit(p, 100, "t"));
            Assert.AreEqual(100, economy.Credit(p, 100, "t"));
            Assert.AreEqual(50, economy.Credit(p, 100, "t"));
            Assert.AreEqual(250, p.Coins);
            Assert.AreEqual(50, p.CoinsLostToCap);

            Assert.IsTrue(economy.TryDebit(p, 200, "t"));
            Assert.IsFalse(economy.TryDebit(p, 100, "t"));
            Assert.AreEqual(50, p.Coins);
            Assert.IsFalse(economy.TryDebit(p, -5, "t"));
        }

        // ------------------------------------------------------------------ modifiers

        [Test]
        public void Modifiers_CurveLeftReplacesCurveRight()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var c = new ModifierController();
            c.Apply(Def(catalog, ModifierCatalog.CurveRight), 0, 1, "e1", 1, 1, 2);
            c.Apply(Def(catalog, ModifierCatalog.CurveLeft), 0, 1, "e2", 1, 2, 2);
            Assert.AreEqual(1, c.Active.Count);
            Assert.AreEqual(ModifierCatalog.CurveLeft, c.Active[0].Definition.Id);
        }

        [Test]
        public void Modifiers_OnlyCameraRollEffectsAreExclusive()
        {
            // Aranha e Invertido rodam os dois a câmara: o mais recente substitui o outro (regra explícita).
            var catalog = ModifierCatalog.CreateDefaults();
            var c = new ModifierController();
            c.Apply(Def(catalog, ModifierCatalog.UpsideDown), 0, 1, "e1", 1, 1, 4);
            c.Apply(Def(catalog, ModifierCatalog.Drunk), 0, 1, "e2", 1, 2, 4);
            c.Apply(Def(catalog, ModifierCatalog.SpiderMode), 0, 1, "e3", 1, 3, 4);
            Assert.AreEqual(2, c.Active.Count, "Bêbado fica; Aranha substitui Invertido");
            Assert.IsTrue(c.Active.Any(m => m.Definition.Id == ModifierCatalog.Drunk));
            Assert.IsTrue(c.Active.Any(m => m.Definition.Id == ModifierCatalog.SpiderMode));
        }

        [Test]
        public void Modifiers_HeavyBallIntensityIsCapped()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var heavy = Def(catalog, ModifierCatalog.HeavyBall);
            var c = new ModifierController();
            for (int i = 0; i < 4; i++) c.Apply(heavy, 0, 1, "e", 1, i + 1, 2);
            Assert.AreEqual(1, c.Active.Count);
            Assert.AreEqual(2, c.Active[0].Intensity);

            var shot = ShotModifierPipeline.Apply(ShotParameters.FromInput(Vector2.right, 1f, 0f, 0f), c.Active);
            Assert.AreEqual(0.62f * 0.62f, shot.ImpulseScale, 1e-4f);
        }

        [Test]
        public void Modifiers_GameplayLimitRejectsThirdEffect()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var c = new ModifierController();
            Assert.IsNotNull(c.Apply(Def(catalog, ModifierCatalog.CurveRight), 0, 1, "e", 1, 1, 2));
            Assert.IsNotNull(c.Apply(Def(catalog, ModifierCatalog.HeavyBall), 0, 1, "e", 1, 2, 2));
            Assert.IsFalse(c.CanApply(Def(catalog, ModifierCatalog.SpiderMode), 2, out var reason));
            Assert.IsNotNull(reason);
            // Um efeito do mesmo grupo substitui e por isso continua permitido.
            Assert.IsTrue(c.CanApply(Def(catalog, ModifierCatalog.CurveLeft), 2, out _));
        }

        [Test]
        public void Modifiers_ShotDurationExpiresAfterTick()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var c = new ModifierController();
            c.Apply(Def(catalog, ModifierCatalog.CurveRight), 0, 1, "e", 1, 1, 2);
            Assert.AreEqual(0, c.Tick(ModifierDurationType.Turns).Count);
            // Dura as próximas ModifierCatalog.DebuffShots tacadas do próprio jogador e sai na última.
            for (int shot = 1; shot < ModifierCatalog.DebuffShots; shot++)
            {
                Assert.AreEqual(0, c.Tick(ModifierDurationType.Shots).Count, "ainda activo após a tacada " + shot);
                Assert.AreEqual(ModifierCatalog.DebuffShots - shot, c.Active[0].RemainingDuration);
            }
            Assert.AreEqual(1, c.Tick(ModifierDurationType.Shots).Count);
            Assert.AreEqual(0, c.Active.Count);
        }

        [Test]
        public void CameraPipeline_ComfortModeMovesSwayToAim()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var c = new ModifierController();
            c.Apply(Def(catalog, ModifierCatalog.Drunk), 0, 1, "e", 1, 1, 2);

            var full = CameraModifierPipeline.Build(c.Active, false);
            var comfort = CameraModifierPipeline.Build(c.Active, true);
            Assert.Greater(full.Sway, 0f);
            Assert.AreEqual(0f, comfort.Sway);
            Assert.Greater(comfort.AimSway, full.AimSway);
        }

        [Test]
        public void ShotPipeline_CurveIsSignedAndHasDuration()
        {
            var catalog = ModifierCatalog.CreateDefaults();
            var c = new ModifierController();
            c.Apply(Def(catalog, ModifierCatalog.CurveLeft), 0, 1, "e", 1, 1, 2);
            var shot = ShotModifierPipeline.Apply(ShotParameters.FromInput(Vector2.right, 0.5f, 0f, 0f), c.Active);
            Assert.Less(shot.CurveAcceleration, 0f);
            Assert.Greater(shot.CurveDuration, 0f);
            Assert.AreEqual(1f, shot.ImpulseScale);
        }

        // ------------------------------------------------------------------ sabotage

        sealed class SabotageFixture
        {
            public readonly List<PlayerState> Players = new List<PlayerState>();
            public readonly EconomyService Economy;
            public readonly SabotageService Sabotage;
            public readonly MatchConfig Config;

            public SabotageFixture(int players, int immunityWindows = 1)
            {
                Config = ScriptableObject.CreateInstance<MatchConfig>();
                Config.ImmunityWindowsAfterSabotage = immunityWindows;
                var rng = new RandomService(42, null);
                for (int i = 0; i < players; i++) Players.Add(new PlayerState(i, new PlayerProfile("P" + i, Archetype.Circle, Color.white), 2));
                Economy = new EconomyService(Config.CoinCap);
                var modifiers = new ModifierService(ModifierCatalog.CreateDefaults(), Config, rng, null);
                Sabotage = new SabotageService(Config, Economy, modifiers, rng, null, id => Players[id]);
            }

            public PurchaseResult Buy(string requestId, int buyer, int target, int tier = 0)
            {
                return Sabotage.Submit(new PurchaseRequest { ClientRequestId = requestId, BuyerId = buyer, TargetId = target, Tier = tier }, 1);
            }
        }

        [Test]
        public void Sabotage_ValidatesSelfAndBalance()
        {
            var f = new SabotageFixture(3);
            f.Sabotage.OpenWindow(1);
            f.Economy.Credit(f.Players[0], 50, "t");
            Assert.AreEqual("Não te podes sabotar", f.Sabotage.ValidateTarget(f.Players[0], f.Players[0]));
            Assert.AreEqual(PurchaseStatus.Rejected, f.Buy("a", 0, 1).Status);
            Assert.AreEqual(50, f.Players[0].Coins);
        }

        [Test]
        public void Sabotage_AcceptedPurchaseDebitsAppliesAndGrantsImmunity()
        {
            var f = new SabotageFixture(3);
            f.Sabotage.OpenWindow(1);
            f.Economy.Credit(f.Players[0], f.Config.SabotageCost, "t");
            f.Economy.Credit(f.Players[2], 100, "t");

            var r = f.Buy("a", 0, 1);
            Assert.AreEqual(PurchaseStatus.Accepted, r.Status);
            Assert.AreEqual(0, f.Players[0].Coins);
            Assert.AreEqual(1, f.Players[1].Modifiers.Active.Count);
            Assert.AreEqual(0, f.Players[1].Modifiers.Active[0].SourcePlayerId);

            var second = f.Buy("b", 2, 1);
            Assert.AreEqual(PurchaseStatus.Rejected, second.Status);
            Assert.AreEqual("Alvo imune", second.Reason);
            Assert.AreEqual(100, f.Players[2].Coins, "compra rejeitada não pode debitar");
        }

        [Test]
        public void Sabotage_DuplicateRequestDoesNotChargeTwice()
        {
            var f = new SabotageFixture(3);
            f.Sabotage.OpenWindow(1);
            f.Economy.Credit(f.Players[0], 250, "t");
            Assert.AreEqual(PurchaseStatus.Accepted, f.Buy("same", 0, 1).Status);
            Assert.AreEqual(PurchaseStatus.Duplicate, f.Buy("same", 0, 1).Status);
            Assert.AreEqual(250 - f.Config.SabotageCost, f.Players[0].Coins);
        }

        [Test]
        public void Sabotage_OnePurchasePerWindowAndRepeatTargetCostsMore()
        {
            var f = new SabotageFixture(3, immunityWindows: 0);
            f.Sabotage.OpenWindow(1);
            f.Economy.Credit(f.Players[0], 250, "t");
            Assert.AreEqual(PurchaseStatus.Accepted, f.Buy("a", 0, 1).Status);
            Assert.AreEqual("Máquina a recarregar", f.Buy("b", 0, 2).Reason);

            f.Sabotage.CloseWindow();
            f.Sabotage.OpenWindow(2);
            int baseCost = f.Config.SabotageCost;
            Assert.AreEqual(Mathf.RoundToInt(baseCost * f.Config.RepeatTargetCostMultiplier), f.Sabotage.CostFor(f.Players[0], f.Players[1]));
            Assert.AreEqual(baseCost, f.Sabotage.CostFor(f.Players[0], f.Players[2]));
            Assert.AreEqual(Mathf.RoundToInt(baseCost * f.Config.ChaosCostMultiplier), f.Sabotage.CostFor(f.Players[0], f.Players[2], 1));
        }

        [Test]
        public void Sabotage_TierDecidesTheEffectPool()
        {
            var f = new SabotageFixture(3, immunityWindows: 0);
            f.Sabotage.OpenWindow(1);
            f.Economy.Credit(f.Players[0], 250, "t");
            f.Economy.Credit(f.Players[2], 250, "t");
            Assert.AreEqual(PurchaseStatus.Accepted, f.Buy("chaos", 0, 1, 1).Status);
            Assert.AreEqual(SabotageTier.Chaos, f.Players[1].Modifiers.Active[0].Definition.Tier);
            Assert.AreEqual(250 - Mathf.RoundToInt(f.Config.SabotageCost * f.Config.ChaosCostMultiplier), f.Players[0].Coins);
            Assert.AreEqual(PurchaseStatus.Accepted, f.Buy("prank", 2, 0, 0).Status);
            Assert.AreEqual(SabotageTier.Prank, f.Players[0].Modifiers.Active[0].Definition.Tier);
        }

        [Test]
        public void Sabotage_ClosedWindowAndEliminatedTargetAreRejected()
        {
            var f = new SabotageFixture(3);
            f.Economy.Credit(f.Players[0], 250, "t");
            Assert.AreEqual("Loja fechada", f.Buy("a", 0, 1).Reason);
            f.Sabotage.OpenWindow(1);
            f.Players[1].Eliminated = true;
            Assert.AreEqual("Alvo eliminado", f.Buy("b", 0, 1).Reason);
            Assert.AreEqual(250, f.Players[0].Coins);
        }

        // ------------------------------------------------------------------ punishment & order

        [Test]
        public void Punishment_WinnerAndEliminatedAreNeverEligible()
        {
            var players = new List<PlayerState>();
            for (int i = 0; i < 4; i++) players.Add(new PlayerState(i, new PlayerProfile("P" + i, Archetype.Circle, Color.white), 1));
            players[3].Eliminated = true;
            var eligible = PunishmentRules.BuildEligible(players, 1, 1, false);
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, eligible);
        }

        [Test]
        public void Punishment_ImmunityIsIgnoredWhenItWouldEmptyThePool()
        {
            var players = new List<PlayerState>();
            for (int i = 0; i < 3; i++) players.Add(new PlayerState(i, new PlayerProfile("P" + i, Archetype.Circle, Color.white), 1));
            players[0].ImmuneUntilWindow = 5;
            players[2].ImmuneUntilWindow = 5;
            var eligible = PunishmentRules.BuildEligible(players, 1, 3, true);
            CollectionAssert.AreEquivalent(new[] { 0, 2 }, eligible);
        }

        [Test]
        public void TurnOrder_OpeningPlayerRotatesAndSkipsEliminated()
        {
            var baseOrder = new List<int> { 2, 0, 3, 1 };
            int cursor = -1;
            var all = new HashSet<int> { 0, 1, 2, 3 };
            CollectionAssert.AreEqual(new[] { 2, 0, 3, 1 }, PunishmentRules.RotatedOrder(baseOrder, all, ref cursor));
            CollectionAssert.AreEqual(new[] { 0, 3, 1, 2 }, PunishmentRules.RotatedOrder(baseOrder, all, ref cursor));
            var withoutThree = new HashSet<int> { 0, 1, 2 };
            CollectionAssert.AreEqual(new[] { 1, 2, 0 }, PunishmentRules.RotatedOrder(baseOrder, withoutThree, ref cursor));
        }
    }
}
