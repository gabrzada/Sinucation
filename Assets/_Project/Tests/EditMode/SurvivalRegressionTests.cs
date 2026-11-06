using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class SurvivalRegressionTests
    {
        [Test]
        public void EveryShootingStanceRemainsOutsideBotNavigationObstacle()
        {
            var table = new TableGeometry(new BilliardsPhysicsSettings());
            for (int x = -4; x <= 4; x++)
            for (int y = -4; y <= 4; y++)
            for (int degrees = 0; degrees < 360; degrees += 3)
            {
                var cue = new Vector2(x * table.HalfLength / 5f, y * table.HalfWidth / 5f);
                float angle = degrees * Mathf.Deg2Rad;
                var stand = ShotMath.StandBehind(cue, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)), table);
                Assert.IsTrue(Mathf.Abs(stand.x) > table.HalfLength + .3f || Mathf.Abs(stand.y) > table.HalfWidth + .3f,
                    $"Unreachable stance: cue={cue}, angle={degrees}, stand={stand}");
            }
        }

        [Test]
        public void RiskIncreasesOnlyForThePlayerWhoSurvives()
        {
            var risk = new RiskBag(1, 6, new RandomService(21, null), 1.4f);
            var a = new PlayerState(0, new PlayerProfile("A", Archetype.Circle, Color.white), 1);
            var b = new PlayerState(1, new PlayerProfile("B", Archetype.Square, Color.white), 1);
            float original = risk.ChanceAfterSurvivals(b.RiskSurvivals);
            for (int i = 0; i < 10; i++)
            {
                float before = risk.ChanceAfterSurvivals(a.RiskSurvivals);
                var outcome = risk.DrawFor(a);
                Assert.That(a.RiskAttempts, Is.EqualTo(i + 1));
                if (outcome == RiskOutcome.Debuff) Assert.That(risk.ChanceAfterSurvivals(a.RiskSurvivals), Is.GreaterThan(before));
                Assert.That(b.RiskAttempts, Is.Zero);
                Assert.That(risk.ChanceAfterSurvivals(b.RiskSurvivals), Is.EqualTo(original));
            }
        }

        [TestCase(3, 0x10de)] [TestCase(4, 0x1002)] [TestCase(5, 0x8086)]
        public void NativeUpscalerIsRestrictedToItsVendor(int index, int vendor)
        {
            Assert.IsTrue(UpscalerController.MatchesVendor(index, vendor));
            foreach (int other in new[] { 0x10de, 0x1002, 0x8086, 0 })
                if (other != vendor) Assert.IsFalse(UpscalerController.MatchesVendor(index, other));
        }
    }
}
