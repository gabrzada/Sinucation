using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core.Modifiers;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Bocas das caçapas (jaws): entram as trajectórias coerentes (de frente, coladas à tabela), as tortas batem nas
    /// bocas e saem; nada é puxado para a caçapa no pano. Valores afinados com o varrimento em Docs/DECISOES.md.
    /// </summary>
    public class PocketJawTests
    {
        static float Power(BilliardsPhysicsSettings s, float speed)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (s.CueSpeedForPower(mid) < speed) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        static BilliardsSimulation Roll(Vector2 start, Vector2 dir, float speed)
        {
            var s = new BilliardsPhysicsSettings();
            var sim = new BilliardsSimulation(s);
            sim.SetupCustom(new[] { new KeyValuePair<int, Vector2>(0, start) }, 0);
            sim.StrikeCue(ShotParameters.FromInput(dir.normalized, Power(s, speed), 0f, 0f), new ShotRecord());
            for (int i = 0; i < 6000 && sim.IsSimulating; i++) sim.Advance(1f / 60f);
            return sim;
        }

        [Test]
        public void EveryPocketHasTwoJawsAndANarrowerThroat()
        {
            var t = new TableGeometry(new BilliardsPhysicsSettings());
            Assert.AreEqual(12, t.Jaws.Count);
            Assert.AreEqual(t.Cushions.Count + t.Jaws.Count, t.Walls.Count);
            for (int i = 0; i < t.Pockets.Count; i++)
            {
                var p = t.Pockets[i];
                float mouth = (t.Jaws[i * 2].A - t.Jaws[i * 2 + 1].A).magnitude;
                float throat = (p.ThroatA - p.ThroatB).magnitude;
                Assert.Less(throat, mouth, "caçapa " + i);
                Assert.Greater(throat - 2f * t.BallRadius, 0.02f, "a bola passa na garganta (" + i + ")");
            }
        }

        [TestCase(1f)] [TestCase(2f)] [TestCase(4f)]
        public void BallRollingAlongTheRailDropsIntoTheCorner(float speed)
        {
            var s = new BilliardsPhysicsSettings();
            float hl = s.TableLength * 0.5f, hw = s.TableWidth * 0.5f;
            var sim = Roll(new Vector2(-hl + 0.6f, hw - s.BallRadius - 0.005f), Vector2.left, speed);
            Assert.IsTrue(sim.Cue.Pocketed);
            Assert.AreEqual(0, sim.Cue.PocketIndex);
        }

        [TestCase(1.5f)] [TestCase(3f)]
        public void StraightIntoTheSidePocketDrops(float speed)
        {
            var s = new BilliardsPhysicsSettings();
            var sim = Roll(new Vector2(0f, 0.1f), Vector2.up, speed);
            Assert.IsTrue(sim.Cue.Pocketed);
            Assert.AreEqual(1, sim.Cue.PocketIndex);
        }

        [TestCase(1.5f)] [TestCase(3f)]
        public void SteepSideShotRattlesOut(float speed)
        {
            // 60° em relação à perpendicular, apontada ao centro da boca: numa mesa real não entra.
            var s = new BilliardsPhysicsSettings();
            float hw = s.TableWidth * 0.5f;
            float a = 60f * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
            var sim = Roll(new Vector2(0f, hw) - dir * 0.45f, dir, speed);
            Assert.IsFalse(sim.Cue.Pocketed && sim.Cue.PocketIndex == 1, "não cai na lateral");
        }

        [Test]
        public void SlowBallThatPassedTheNoseRollsIn()
        {
            // Mal passa o nariz das tabelas, já sem força: a base inclinada da caçapa leva-a para dentro.
            var s = new BilliardsPhysicsSettings();
            float hw = s.TableWidth * 0.5f;
            var sim = Roll(new Vector2(0f, hw - 0.2f), Vector2.up, 0.62f);
            Assert.IsTrue(sim.Cue.Pocketed, "não fica parada na entrada da caçapa");
        }

        [Test]
        public void NothingIsPulledOnTheFelt()
        {
            var sim = new BilliardsSimulation(new BilliardsPhysicsSettings());
            var t = sim.Table;
            // Mesmo encostada ao nariz de uma caçapa, dentro da linha das tabelas nunca cai.
            Assert.AreEqual(-1, sim.PocketAt(new Vector2(-t.HalfLength + 0.01f, t.HalfWidth - 0.01f)));
            Assert.AreEqual(-1, sim.PocketAt(new Vector2(0f, t.HalfWidth - 0.001f)));
            // Entre as bocas mas antes da garganta também não (ainda pode tremer e sair).
            Assert.AreEqual(-1, sim.PocketAt(new Vector2(0f, t.HalfWidth + 0.005f)));
            Assert.AreEqual(1, sim.PocketAt(new Vector2(0f, t.HalfWidth + 0.05f)));
        }
    }
}
