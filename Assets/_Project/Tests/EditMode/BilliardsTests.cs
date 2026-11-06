using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class BilliardsTests
    {
        static BilliardsSimulation NewSim() => new BilliardsSimulation(new BilliardsPhysicsSettings());

        static void RunToRest(BilliardsSimulation sim)
        {
            for (int i = 0; i < 4000 && sim.IsSimulating; i++) sim.Advance(1f / 60f);
            Assert.IsFalse(sim.IsSimulating, "a simulação devia convergir para repouso");
        }

        static ShotRecord Shoot(BilliardsSimulation sim, Vector2 direction, float power, float spinX = 0f, float spinY = 0f, float curve = 0f)
        {
            var record = new ShotRecord { LowestBallAtStart = sim.LowestBallOnTable(), FinalBall = sim.FinalBall };
            var shot = ShotParameters.FromInput(direction, power, spinX, spinY);
            shot.CurveAcceleration = curve;
            shot.CurveDuration = curve != 0f ? 1.6f : 0f;
            sim.StrikeCue(shot, record);
            RunToRest(sim);
            return record;
        }

        [Test]
        public void StraightShot_PotsObjectBallInCornerPocket()
        {
            var sim = NewSim();
            var pocket = sim.Table.Pockets[2].Center;
            var cue = new Vector2(0.2f, 0f);
            var dir = (pocket - cue).normalized;
            sim.SetupCustom(new[]
            {
                new KeyValuePair<int, Vector2>(0, cue),
                new KeyValuePair<int, Vector2>(1, cue + dir * 0.4f),
            }, 1);

            var record = Shoot(sim, dir, 0.45f);

            Assert.AreEqual(1, record.FirstContact);
            CollectionAssert.Contains(record.Pocketed, 1);
            Assert.IsFalse(record.CueScratched);
            Assert.IsTrue(PartyNineRules.Evaluate(record).RoundWon);
        }

        [Test]
        public void CueIntoPocketIsScratchFoul()
        {
            var sim = NewSim();
            var pocket = sim.Table.Pockets[0].Center;
            var cue = new Vector2(-0.3f, 0f);
            sim.SetupCustom(new[]
            {
                new KeyValuePair<int, Vector2>(0, cue),
                new KeyValuePair<int, Vector2>(1, new Vector2(0.8f, -0.3f)),
            }, 1);

            var record = Shoot(sim, (pocket - cue).normalized, 0.5f);
            Assert.IsTrue(record.CueScratched);
            var outcome = PartyNineRules.Evaluate(record);
            Assert.AreEqual(FoulType.Scratch, outcome.Foul);
            Assert.IsTrue(outcome.BallInHandNext);
        }

        [Test]
        public void Break_SettlesWithAllBallsValid()
        {
            foreach (int balls in new[] { 4, 6, 9 })
            {
                var sim = NewSim();
                sim.SetupRack(balls, new RandomService(balls, null));
                Assert.AreEqual(balls + 1, sim.Balls.Count);
                Assert.AreEqual(1, sim.LowestBallOnTable());

                var record = Shoot(sim, (sim.GetBall(1).Position - sim.Cue.Position).normalized, 1f);
                Assert.AreEqual(1, record.FirstContact, balls + " bolas");
                foreach (var b in sim.Balls)
                {
                    if (b.Pocketed) continue;
                    Assert.IsFalse(float.IsNaN(b.Position.x) || float.IsNaN(b.Position.y));
                    Assert.IsTrue(sim.Table.IsInsidePlayfield(b.Position, -0.05f), "bola " + b.Number + " fora da mesa: " + b.Position);
                }
            }
        }

        [Test]
        public void SameInputsProduceSameResult()
        {
            Vector2[] Run()
            {
                var sim = NewSim();
                sim.SetupRack(9, new RandomService(99, null));
                Shoot(sim, (sim.GetBall(1).Position - sim.Cue.Position).normalized, 0.9f, 0.3f, -0.4f);
                var result = new Vector2[sim.Balls.Count];
                for (int i = 0; i < result.Length; i++) result[i] = sim.Balls[i].Position;
                return result;
            }

            CollectionAssert.AreEqual(Run(), Run());
        }

        [Test]
        public void CurveModifier_DeflectsToTheRequestedSide()
        {
            float FinalY(float curve)
            {
                var sim = NewSim();
                sim.SetupCustom(new[] { new KeyValuePair<int, Vector2>(0, new Vector2(-0.8f, 0f)), new KeyValuePair<int, Vector2>(1, new Vector2(1f, 0.5f)) }, 1);
                var record = new ShotRecord();
                var shot = ShotParameters.FromInput(Vector2.right, 0.35f, 0f, 0f);
                shot.CurveAcceleration = curve;
                shot.CurveDuration = 1.6f;
                sim.StrikeCue(shot, record);
                for (int i = 0; i < 25; i++) sim.Advance(1f / 60f);
                return sim.Cue.Position.y;
            }

            // Olhando na direcção da tacada (+X), a direita é -Y no plano da mesa.
            Assert.Less(FinalY(0.9f), -0.01f);
            Assert.Greater(FinalY(-0.9f), 0.01f);
            Assert.AreEqual(0f, FinalY(0f), 1e-5f);
        }

        [Test]
        public void HeavyBall_ReducesTravelDistance()
        {
            float Travel(float impulse)
            {
                var sim = NewSim();
                sim.SetupCustom(new[] { new KeyValuePair<int, Vector2>(0, new Vector2(-0.9f, 0f)), new KeyValuePair<int, Vector2>(1, new Vector2(0.9f, 0.5f)) }, 1);
                var shot = ShotParameters.FromInput(Vector2.right, 0.2f, 0f, 0f);
                shot.ImpulseScale = impulse;
                sim.StrikeCue(shot, new ShotRecord());
                RunToRest(sim);
                return sim.Cue.Position.x + 0.9f;
            }

            Assert.Less(Travel(0.62f), Travel(1f) * 0.6f);
        }

        [Test]
        public void Rules_WrongBallFirstIsFoulAndFinalBallRespots()
        {
            var r = new ShotRecord { LowestBallAtStart = 2, FinalBall = 9, FirstContact = 5 };
            r.Pocketed.Add(9);
            var o = PartyNineRules.Evaluate(r);
            Assert.AreEqual(FoulType.WrongBallFirst, o.Foul);
            Assert.IsTrue(o.RespotFinalBall);
            Assert.IsFalse(o.RoundWon);
        }

        [Test]
        public void Rules_LegalComboOnFinalBallWins()
        {
            var r = new ShotRecord { LowestBallAtStart = 3, FinalBall = 9, FirstContact = 3 };
            r.Pocketed.Add(9);
            Assert.IsTrue(PartyNineRules.Evaluate(r).RoundWon);
        }

        [Test]
        public void Rules_LegalPocketKeepsTurnAndMissPasses()
        {
            var pocket = new ShotRecord { LowestBallAtStart = 1, FinalBall = 9, FirstContact = 1 };
            pocket.Pocketed.Add(4);
            Assert.IsTrue(PartyNineRules.Evaluate(pocket).KeepTurn);

            var miss = new ShotRecord { LowestBallAtStart = 1, FinalBall = 9, FirstContact = 1 };
            var o = PartyNineRules.Evaluate(miss);
            Assert.IsFalse(o.KeepTurn);
            Assert.IsFalse(o.IsFoul);

            var none = new ShotRecord { LowestBallAtStart = 1, FinalBall = 9 };
            Assert.AreEqual(FoulType.NoContact, PartyNineRules.Evaluate(none).Foul);
        }

        [Test]
        public void RespotBall_FindsFreeSpotWhenFootSpotIsTaken()
        {
            var sim = NewSim();
            sim.SetupCustom(new[] { new KeyValuePair<int, Vector2>(0, new Vector2(-0.5f, 0f)), new KeyValuePair<int, Vector2>(3, sim.Table.FootSpot), new KeyValuePair<int, Vector2>(9, new Vector2(0, 0)) }, 9);
            sim.GetBall(9).Pocketed = true;
            sim.RespotBall(9);
            var nine = sim.GetBall(9);
            Assert.IsFalse(nine.Pocketed);
            Assert.GreaterOrEqual((nine.Position - sim.GetBall(3).Position).magnitude, sim.Settings.BallRadius * 2f);
        }
    }
}
