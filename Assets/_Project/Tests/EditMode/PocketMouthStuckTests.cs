using System;
using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core.Modifiers;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Relato de 2026-11-02: "uma pequena parte da bola parece impedir que ela entre". Bolas lentas ficavam paradas na
    /// boca com o centro por cima do buraco (canto: entre a garganta e o canto do pano; base: congeladas abaixo de
    /// StopSpeed, com a física a correr até 25 s) e o forro escuro da caçapa subia 5 cm acima do pano, cortando a bola.
    /// Ver Docs/DECISOES.md, "Bola presa na boca da caçapa".
    /// </summary>
    public class PocketMouthStuckTests
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

        static BilliardsSimulation Run(BilliardsSimulation sim)
        {
            for (int i = 0; i < 6000 && sim.IsSimulating; i++) sim.Advance(1f / 60f);
            return sim;
        }

        /// <summary>Bola sozinha a rolar para a garganta da caçapa, rodada <paramref name="angle"/> graus da entrada de frente.</summary>
        static BilliardsSimulation RollIntoPocket(int pocketIndex, float angle, float offset, float speed)
        {
            var s = new BilliardsPhysicsSettings();
            var sim = new BilliardsSimulation(s);
            var p = sim.Table.Pockets[pocketIndex];
            Vector2 axis = p.IsCorner ? new Vector2(Mathf.Sign(p.Center.x), Mathf.Sign(p.Center.y)).normalized : new Vector2(0f, Mathf.Sign(p.Center.y));
            float a = angle * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(axis.x * Mathf.Cos(a) - axis.y * Mathf.Sin(a), axis.x * Mathf.Sin(a) + axis.y * Mathf.Cos(a));
            Vector2 target = (p.ThroatA + p.ThroatB) * 0.5f + new Vector2(-dir.y, dir.x) * offset;
            sim.SetupCustom(new[] { new KeyValuePair<int, Vector2>(0, target - dir * 0.35f) }, 0);
            sim.StrikeCue(ShotParameters.FromInput(dir, Power(s, speed), 0f, 0f), new ShotRecord());
            return Run(sim);
        }

        /// <summary>Bola quase parada (5 mm/s) em <paramref name="position"/>.</summary>
        static BilliardsSimulation Nudge(Vector2 position, Vector2 direction)
        {
            var s = new BilliardsPhysicsSettings();
            var sim = new BilliardsSimulation(s);
            sim.SetupCustom(new[] { new KeyValuePair<int, Vector2>(0, position) }, 0);
            sim.StrikeCue(ShotParameters.FromInput(direction, 0f, 0f, 0f), new ShotRecord());
            sim.Cue.Velocity = direction.normalized * 0.005f;
            return Run(sim);
        }

        [Test]
        public void ReportedSlowCornerBallDropsInsteadOfStoppingPastTheThroat()
        {
            // Caso reproduzido: a 20° da diagonal, 0,512 m/s, a bola parava em (1,119; 0,560), 2,3 cm do centro da caçapa.
            var sim = RollIntoPocket(2, -20f, 0.005f, 0.512f);
            Assert.IsTrue(sim.Cue.Pocketed, "parou em " + sim.Cue.Position.ToString("F4"));
            Assert.AreEqual(2, sim.Cue.PocketIndex);
            Assert.Less(sim.SimulatedTime, 3f);
        }

        [Test]
        public void NoSlowBallStopsWithItsCentrePastTheThroat()
        {
            var t = new TableGeometry(new BilliardsPhysicsSettings());
            foreach (int pocket in new[] { 2, 1 })
                for (float angle = -40f; angle <= 40f; angle += 10f)
                    for (float speed = 0.45f; speed <= 0.9f; speed += 0.0125f)
                    {
                        var sim = RollIntoPocket(pocket, angle, 0.005f, speed);
                        var b = sim.Cue;
                        string label = "caçapa " + pocket + ", " + angle + "°, " + speed.ToString("0.000") + " m/s → " + b.Position.ToString("F4");
                        Assert.IsFalse(sim.IsSimulating, "a física não acabou: " + label);
                        if (b.Pocketed) continue;
                        Assert.IsFalse(t.Pockets[pocket].PastThroat(b.Position), "parada depois da garganta: " + label);
                        Assert.IsTrue(Mathf.Abs(b.Position.x) <= t.HalfLength && Mathf.Abs(b.Position.y) <= t.HalfWidth, "parada fora do pano: " + label);
                    }
        }

        [Test]
        public void RestingBallPastTheCornerThroatRollsIn()
        {
            // O cantinho do pano entre a garganta e o canto: sem a base inclinada aí, a bola ficava lá para sempre.
            var t = new TableGeometry(new BilliardsPhysicsSettings());
            for (int i = 0; i < t.Pockets.Count; i++)
            {
                var p = t.Pockets[i];
                if (!p.IsCorner) continue;
                Vector2 corner = new Vector2(Mathf.Sign(p.Center.x) * t.HalfLength, Mathf.Sign(p.Center.y) * t.HalfWidth);
                int tested = 0;
                for (float dx = 0.0005f; dx < 0.02f; dx += 0.003f)
                    for (float dy = 0.0005f; dy < 0.02f; dy += 0.003f)
                    {
                        Vector2 at = corner - new Vector2(Mathf.Sign(p.Center.x) * dx, Mathf.Sign(p.Center.y) * dy);
                        if (!p.PastThroat(at)) continue;
                        tested++;
                        foreach (var dir in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
                        {
                            var sim = Nudge(at, dir);
                            Assert.IsTrue(sim.Cue.Pocketed, "caçapa " + i + ": parada em " + sim.Cue.Position.ToString("F4") + " (partiu de " + at.ToString("F4") + ")");
                            Assert.AreEqual(i, sim.Cue.PocketIndex);
                            Assert.Less(sim.SimulatedTime, 3f);
                        }
                    }
                Assert.Greater(tested, 3, "o cantinho depois da garganta existe no pano");
            }
        }

        [Test]
        public void AlmostStoppedBallOnThePocketShelfNeverFreezes()
        {
            // Antes: abaixo de StopSpeed a bola congelava na base inclinada e a tacada só acabava aos 25 s.
            var t = new TableGeometry(new BilliardsPhysicsSettings());
            foreach (var at in new[] { new Vector2(0.01f, t.HalfWidth + 0.004f), new Vector2(-0.015f, -t.HalfWidth - 0.01f), new Vector2(t.HalfLength + 0.005f, t.HalfWidth - 0.03f) })
                foreach (var dir in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down })
                {
                    var sim = Nudge(at, dir);
                    Assert.IsTrue(sim.Cue.Pocketed, "parada em " + sim.Cue.Position.ToString("F4") + " (partiu de " + at.ToString("F4") + ")");
                    Assert.Less(sim.SimulatedTime, 3f);
                }
        }

        [Test]
        public void ResultsAwayFromTheMouthAreUnchanged()
        {
            // Entradas de frente e coladas à tabela (as mesmas de PocketJawTests) e uma bola parada no pano à frente da boca.
            Assert.IsTrue(RollIntoPocket(2, 0f, 0f, 1.5f).Cue.Pocketed);
            Assert.IsTrue(RollIntoPocket(1, 0f, 0f, 1.5f).Cue.Pocketed);
            var t = new TableGeometry(new BilliardsPhysicsSettings());
            var onCloth = Nudge(new Vector2(0f, t.HalfWidth - 0.01f), Vector2.down);
            Assert.IsFalse(onCloth.Cue.Pocketed, "no pano à frente da lateral nada puxa");
        }

        [Test]
        public void DarkPocketLinerStaysOffThePlayingCloth()
        {
            // O forro e o fundo escuros subiam 5 cm acima do pano à frente da boca e "cortavam" a bola que entrava.
            var model = FsModels.Get("table", "TableFrame");
            Assert.That(model, Is.Not.Null);
            int dark = Array.IndexOf(model.Materials, "dark");
            Assert.That(dark, Is.GreaterThanOrEqualTo(0), "o teste tem de inspeccionar o forro escuro");
            var settings = new BilliardsPhysicsSettings();
            float hl = settings.TableLength * 0.5f, hw = settings.TableWidth * 0.5f;
            var positions = model.Mesh.vertices;
            var triangles = model.Mesh.GetTriangles(dark);
            int overCloth = 0;
            for (int k = 0; k < triangles.Length; k += 3)
            {
                Vector3 c = (positions[triangles[k]] + positions[triangles[k + 1]] + positions[triangles[k + 2]]) / 3f;
                if (c.y > TableView.TopY + 0.0005f && Mathf.Abs(c.x) < hl - 0.0001f && Mathf.Abs(c.z) < hw - 0.0001f) overCloth++;
            }
            Assert.That(overCloth, Is.Zero, "nenhuma parte escura da caçapa pode ficar acima do pano dentro da linha das tabelas");
        }
    }
}
