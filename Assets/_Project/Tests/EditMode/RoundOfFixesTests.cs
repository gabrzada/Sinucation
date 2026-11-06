using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Regressões da ronda de correcções de 2026-09-29: física com instante exacto de contacto e previsão da mira
    /// igual à simulação, fim da "bola fora da mesa", bola na mão com validação partilhada cliente/anfitrião,
    /// partidas de 2 participantes, preços da máquina e limites da escala da interface.
    /// </summary>
    public class RoundOfFixesTests
    {
        static BilliardsSimulation NewSim() => new BilliardsSimulation(new BilliardsPhysicsSettings());

        static void Setup(BilliardsSimulation sim, Vector2 cue, params (int n, Vector2 p)[] balls)
        {
            var list = new List<KeyValuePair<int, Vector2>> { new KeyValuePair<int, Vector2>(0, cue) };
            foreach (var b in balls) list.Add(new KeyValuePair<int, Vector2>(b.n, b.p));
            sim.SetupCustom(list, 9);
        }

        /// <summary>Dispara e devolve a direcção de saída da primeira bola tocada, medida na simulação.</summary>
        static Vector2 FirstObjectDirection(BilliardsSimulation sim, ShotParameters shot, out Vector2 cueAtContact)
        {
            Vector2 dir = Vector2.zero;
            Vector2 contact = Vector2.zero;
            bool seen = false;
            sim.Contact += c =>
            {
                if (seen || c.Type != ContactType.BallBall) return;
                seen = true;
                var other = sim.GetBall(c.A == 0 ? c.B : c.A);
                dir = other.Velocity.normalized;
                contact = sim.Cue.Position;
            };
            sim.StrikeCue(shot, new ShotRecord());
            for (int i = 0; i < 4000 && sim.IsSimulating && !seen; i++) sim.Advance(1f / 60f);
            Assert.IsTrue(seen, "a branca devia tocar numa bola");
            cueAtContact = contact;
            return dir;
        }

        [TestCase(0.2f, 0.00f)]
        [TestCase(0.6f, 0.02f)]
        [TestCase(1.0f, 0.045f)]
        [TestCase(1.0f, 0.053f)]
        public void Prediction_MatchesSimulation_EvenOnFastThinCuts(float power, float offset)
        {
            // Corte fino a toda a força: com a colisão antiga (penetração até 1,5 cm) o ângulo de saída desviava vários
            // graus da linha amarela; agora a previsão e a física usam o mesmo instante de contacto.
            var sim = NewSim();
            Setup(sim, new Vector2(-0.6f, 0f), (1, new Vector2(0.2f, offset)), (9, new Vector2(0.9f, 0.4f)));
            var shot = ShotParameters.FromInput(Vector2.right, power, 0f, 0f);
            var path = new List<Vector2>();
            Assert.IsTrue(sim.PredictCue(shot, path, out var p));
            Assert.AreEqual(1, p.Ball);
            var actual = FirstObjectDirection(sim, shot, out var cueAtContact);
            Assert.Less(Vector2.Angle(actual, p.ObjectDirection), 0.3f, "direcção da bola tocada");
            Assert.Less((cueAtContact - p.CueAtContact).magnitude, 0.003f, "bola fantasma");
        }

        [Test]
        public void Prediction_FollowsTheCurveDebuff()
        {
            var shot = ShotParameters.FromInput(Vector2.right, 0.4f, 0f, 0f);
            shot.CurveAcceleration = 0.9f;
            shot.CurveDuration = 1.6f;
            var path = new List<Vector2>();
            // Procura uma posição da bola 1 que só é apanhada por causa da curva (à direita = −Y).
            BilliardsSimulation sim = null;
            AimPrediction p = default;
            bool found = false;
            for (float y = -0.05f; y > -0.4f && !found; y -= 0.01f)
            {
                sim = NewSim();
                Setup(sim, new Vector2(-0.8f, 0f), (1, new Vector2(0.4f, y)), (9, new Vector2(0.9f, 0.45f)));
                found = sim.PredictCue(shot, path, out p) && p.Ball == 1;
            }
            Assert.IsTrue(found, "a curva devia levar a branca até à bola 1");
            var straight = ShotParameters.FromInput(Vector2.right, 0.4f, 0f, 0f);
            Assert.IsFalse(sim.PredictCue(straight, new List<Vector2>(), out var sp) && sp.Ball == 1, "em linha recta não lhe tocava");
            var actual = FirstObjectDirection(sim, shot, out var cueAtContact);
            Assert.Less((cueAtContact - p.CueAtContact).magnitude, 0.003f);
            Assert.Less(Vector2.Angle(actual, p.ObjectDirection), 0.5f);
        }

        [Test]
        public void BallThroughTheCornerJawIsPocketedNeverOffTable()
        {
            // Entre a ponta da tabela e o círculo da caçapa: antes contava como "bola fora da mesa".
            var sim = NewSim();
            var t = sim.Table;
            Setup(sim, new Vector2(-t.HalfLength + 0.04f, t.HalfWidth - 0.2f), (1, new Vector2(0.5f, 0f)), (9, new Vector2(0.9f, 0.3f)));
            var record = new ShotRecord { LowestBallAtStart = 1, FinalBall = 9 };
            sim.StrikeCue(ShotParameters.FromInput(Vector2.up, 0.6f, 0f, 0f), record);
            for (int i = 0; i < 4000 && sim.IsSimulating; i++) sim.Advance(1f / 60f);
            Assert.IsTrue(sim.Cue.Pocketed, "a branca cai na caçapa de canto");
            Assert.IsTrue(record.CueScratched);
            foreach (var b in sim.Balls)
                if (!b.Pocketed) Assert.IsTrue(t.IsInsidePlayfield(b.Position, 0f), "nenhuma bola fica fora da mesa");
        }

        [Test]
        public void HardShotsNeverLeaveTheTable()
        {
            var rng = new System.Random(7);
            for (int k = 0; k < 40; k++)
            {
                var sim = NewSim();
                sim.SetupRack(9, new RandomService(k, null));
                float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                sim.StrikeCue(ShotParameters.FromInput(new Vector2(Mathf.Cos(a), Mathf.Sin(a)), 1f, 0f, 0f), new ShotRecord());
                for (int i = 0; i < 6000 && sim.IsSimulating; i++) sim.Advance(1f / 60f);
                foreach (var b in sim.Balls)
                    if (!b.Pocketed) Assert.IsTrue(sim.Table.IsInsidePlayfield(b.Position, sim.Settings.BallRadius * 0.5f), "tacada " + k + " bola " + b.Number);
            }
        }

        static MatchDirector Match(int players, int seed)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            config.RoundIntroSeconds = 0.1f;
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < players; i++) profiles.Add(new PlayerProfile("P" + i, (Archetype)(i % 3), Color.white));
            var d = new MatchDirector(config, ModifierCatalog.CreateDefaults(), profiles, seed);
            d.Start();
            for (int i = 0; i < 10 && d.Phase != MatchPhase.AwaitShot; i++) d.Tick(0.1f);
            return d;
        }

        [Test]
        public void CuePlacement_ServerValidationMatchesClientPreview()
        {
            var d = Match(2, 3);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            Assert.IsTrue(d.BallInHand && d.BallInHandKitchenOnly, "a abertura começa com bola na mão atrás da linha");
            int me = d.CurrentPlayerId, other = 1 - me;
            var sim = d.Sim;

            // Motivos iguais no cliente (DescribePlacement) e no anfitrião (PlaceCueBall).
            var beyond = new Vector2(sim.Table.HeadStringX + 0.2f, 0f);
            Assert.IsNotNull(sim.DescribePlacement(beyond, true));
            Assert.AreEqual(sim.DescribePlacement(beyond, true), d.PlaceCueBall(me, beyond));
            Assert.IsNotNull(d.PlaceCueBall(other, new Vector2(sim.Table.HeadStringX - 0.2f, 0f)), "só quem tem a vez");

            // Apontar para cima de outra bola: a pré-visualização encaixa no sítio livre mais próximo, que é aceite.
            var blocker = new Vector2(sim.Table.HeadStringX - 0.25f, 0.1f);
            var state = new List<BallState> { new BallState { Number = 0, Position = sim.Table.HeadSpot }, new BallState { Number = 1, Position = blocker }, new BallState { Number = 9, Position = new Vector2(0.8f, 0f) } };
            sim.LoadState(state, 9);
            Assert.IsNotNull(sim.DescribePlacement(blocker, true));
            Assert.IsTrue(sim.TryFindPlacementNear(blocker, true, sim.Settings.BallRadius * 3f, out var snapped));
            Assert.IsNull(d.PlaceCueBall(me, snapped));
            Assert.AreEqual(snapped, sim.Cue.Position);
        }

        [Test]
        public void TwoPlayerMatch_StartsAndSelectsTheLoserForTheRoulette()
        {
            var d = Match(2, 9);
            Assert.AreEqual(2, d.Players.Count);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            var rng = new System.Random(9);
            for (int i = 0; i < 400000 && d.Phase != MatchPhase.PunishmentDraw && d.Phase != MatchPhase.MatchOver; i++)
            {
                if (d.Phase == MatchPhase.AwaitShot)
                {
                    var target = d.Sim.GetBall(d.Sim.LowestBallOnTable());
                    d.SubmitShot(new ShotRequest { PlayerId = d.CurrentPlayerId, ShotSequence = d.ShotSequence, Direction = target.Position - d.Sim.Cue.Position, Power = 0.4f + (float)rng.NextDouble() * 0.5f });
                }
                d.Tick(0.1f);
            }
            Assert.AreEqual(MatchPhase.PunishmentDraw, d.Phase);
            Assert.AreEqual(1, d.PunishmentCandidates.Count, "com 2 jogadores só o que perdeu a ronda vai à roleta");
            Assert.AreNotEqual(d.RoundWinnerId, d.SelectedPlayerId);
        }

        [Test]
        public void SabotageMachineAdvertisesTheRealPrices()
        {
            var d = Match(3, 4);
            var config = d.Config;
            Assert.AreEqual(config.SabotageCost, d.Sabotage.BaseCost(0));
            Assert.AreEqual(Mathf.CeilToInt(config.SabotageCost * config.ChaosCostMultiplier), d.Sabotage.BaseCost(1));
            var buyer = d.GetPlayer(0);
            var target = d.GetPlayer(1);
            buyer.Coins = d.Sabotage.BaseCost(1);
            int cost = d.Sabotage.CostFor(buyer, target, 1);
            var r = d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "x1", BuyerId = 0, TargetId = 1, Tier = 1 });
            Assert.IsTrue(r.Accepted);
            Assert.AreEqual(cost, r.Cost);
            Assert.AreEqual(0, buyer.Coins, "cobrado exactamente o preço anunciado");
            var again = d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "x1", BuyerId = 0, TargetId = 1, Tier = 1 });
            Assert.AreEqual(PurchaseStatus.Duplicate, again.Status);
            Assert.AreEqual(0, buyer.Coins, "um pedido repetido nunca cobra outra vez");
            Assert.AreNotEqual(PurchaseStatus.Accepted, d.SubmitPurchase(new PurchaseRequest { ClientRequestId = "x2", BuyerId = 0, TargetId = 2, Tier = 0 }).Status, "sem saldo");
            Assert.GreaterOrEqual(buyer.Coins, 0);
        }

        [Test]
        public void BotDifficulty_HardPotsMoreThanEasy()
        {
            int Pots(BotDifficulty level)
            {
                int pots = 0;
                var rng = new System.Random(1234);
                for (int k = 0; k < 60; k++)
                {
                    var sim = NewSim();
                    var layout = new System.Random(k);
                    float Rand(float a, float b) => a + (float)layout.NextDouble() * (b - a);
                    Setup(sim, new Vector2(Rand(-0.9f, -0.3f), Rand(-0.4f, 0.4f)), (1, new Vector2(Rand(0f, 0.8f), Rand(-0.4f, 0.4f))), (9, new Vector2(Rand(0.2f, 0.9f), Rand(-0.45f, 0.45f))));
                    var plan = BotBrain.PlanShot(sim, false, false, rng, level);
                    var record = new ShotRecord { LowestBallAtStart = 1, FinalBall = 9 };
                    sim.StrikeCue(ShotParameters.FromInput(plan.Direction, plan.Power, 0f, 0f), record);
                    for (int i = 0; i < 4000 && sim.IsSimulating; i++) sim.Advance(1f / 60f);
                    if (PartyNineRules.Evaluate(record).LegalPockets > 0) pots++;
                }
                return pots;
            }
            int easy = Pots(BotDifficulty.Easy), medium = Pots(BotDifficulty.Medium), hard = Pots(BotDifficulty.Hard);
            Debug.Log("[BotLevels] encaçapadas em 60: fácil " + easy + " · médio " + medium + " · difícil " + hard);
            Assert.Greater(hard, easy, "o Difícil tem de encaçapar mais do que o Fácil");
            Assert.GreaterOrEqual(hard, medium - 2, "o Difícil não pode ser pior do que o Médio");
        }

        [TestCase(1920, 1080)]
        [TestCase(1280, 720)]
        [TestCase(1280, 1024)]
        [TestCase(1024, 768)]
        [TestCase(2560, 1080)]
        [TestCase(3840, 2160)]
        public void UiScaleAlwaysLeavesRoomForTheLayout(int w, int h)
        {
            foreach (float pref in new[] { HudView.UiScaleMin, 1f, HudView.UiScaleMax })
            {
                float s = HudView.EffectiveScale(w, h, pref);
                Assert.GreaterOrEqual(w / s, HudView.MinVirtualWidth - 0.5f, "largura virtual " + w + "x" + h + " @" + pref);
                Assert.GreaterOrEqual(h / s, HudView.MinVirtualHeight - 0.5f, "altura virtual " + w + "x" + h + " @" + pref);
            }
            Assert.Less(HudView.EffectiveScale(w, h, HudView.UiScaleMin), HudView.EffectiveScale(w, h, 1f) + 1e-4f);
            Assert.AreEqual(1f, HudView.EffectiveScale(1920, 1080, 1f), 1e-4f);
        }
    }
}
