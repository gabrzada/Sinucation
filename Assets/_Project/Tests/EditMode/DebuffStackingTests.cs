using System.Collections.Generic;
using System.Linq;
using FriendSlop.Core.Modifiers;
using NUnit.Framework;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Debuffs acumulativos: cada efeito tem o seu estado e as suas tacadas; aplicar um novo não remove os outros
    /// (excepto a regra explícita de rotação de câmara e das curvas opostas).
    /// </summary>
    public class DebuffStackingTests
    {
        const int Limit = 4;
        List<ModifierDefinition> _catalog;
        int _nextId;

        [SetUp]
        public void SetUp()
        {
            _catalog = ModifierCatalog.CreateDefaults();
            _nextId = 0;
        }

        ModifierInstance Apply(ModifierController c, string id) =>
            c.Apply(_catalog.First(d => d.Id == id), 0, 1, "e" + _nextId, 1, ++_nextId, Limit);

        static bool Has(ModifierController c, string id) => c.Active.Any(m => m.Definition.Id == id);

        [TestCase(ModifierCatalog.SpiderMode, ModifierCatalog.Myopia)]
        [TestCase(ModifierCatalog.TunnelVision, ModifierCatalog.SpiderMode)]
        [TestCase(ModifierCatalog.ColorBlindness, ModifierCatalog.Myopia)]
        [TestCase(ModifierCatalog.Drunk, ModifierCatalog.TunnelVision)]
        public void TwoCameraEffectsWorkTogether(string first, string second)
        {
            var c = new ModifierController();
            Assert.IsNotNull(Apply(c, first));
            Assert.IsNotNull(Apply(c, second));
            Assert.IsTrue(Has(c, first) && Has(c, second), first + " + " + second);
        }

        [Test]
        public void SpiderPlusMyopiaComposesBothVisuals()
        {
            var c = new ModifierController();
            Apply(c, ModifierCatalog.SpiderMode);
            Apply(c, ModifierCatalog.Myopia);
            var profile = CameraModifierPipeline.Build(c.Active, false);
            Assert.That(profile.Roll, Is.EqualTo(90f), "mecânica da aranha");
            Assert.That(profile.Myopia, Is.GreaterThan(0f), "desfoque da miopia");
        }

        [Test]
        public void ThreeOrMoreEffectsOfDifferentKindsStack()
        {
            var c = new ModifierController();
            Apply(c, ModifierCatalog.SpiderMode);
            Apply(c, ModifierCatalog.Myopia);
            Apply(c, ModifierCatalog.ColorBlindness);
            Apply(c, ModifierCatalog.HeavyBall);
            Assert.AreEqual(4, c.Active.Count);
            var profile = CameraModifierPipeline.Build(c.Active, false);
            Assert.That(profile.Roll, Is.EqualTo(90f));
            Assert.That(profile.Myopia, Is.GreaterThan(0f));
            Assert.That(profile.Deuteranopia, Is.GreaterThan(0f));
            var shot = ShotModifierPipeline.Apply(ShotParameters.FromInput(UnityEngine.Vector2.right, 1f, 0f, 0f), c.Active);
            Assert.That(shot.ImpulseScale, Is.LessThan(1f), "efeito de tacada continua com os de câmara");
        }

        [Test]
        public void RepeatingADebuffRenewsOnlyItsOwnTimer()
        {
            var c = new ModifierController();
            Apply(c, ModifierCatalog.Myopia);
            Apply(c, ModifierCatalog.SpiderMode);
            c.Tick(ModifierDurationType.Shots);
            var spiderBefore = c.Active.First(m => m.Definition.Id == ModifierCatalog.SpiderMode).RemainingDuration;
            var again = Apply(c, ModifierCatalog.Myopia);
            Assert.AreEqual(ModifierCatalog.DebuffShots, again.RemainingDuration, "miopia renovada");
            Assert.AreEqual(spiderBefore, c.Active.First(m => m.Definition.Id == ModifierCatalog.SpiderMode).RemainingDuration, "aranha intacta");
            Assert.AreEqual(2, c.Active.Count, "sem duplicados");
        }

        [Test]
        public void ExpiringOneEffectKeepsTheOthers()
        {
            var c = new ModifierController();
            Apply(c, ModifierCatalog.TunnelVision);
            c.Tick(ModifierDurationType.Shots);
            Apply(c, ModifierCatalog.SpiderMode);
            // O túnel acaba uma tacada antes da aranha.
            for (int i = 1; i < ModifierCatalog.DebuffShots; i++) c.Tick(ModifierDurationType.Shots);
            Assert.IsFalse(Has(c, ModifierCatalog.TunnelVision));
            Assert.IsTrue(Has(c, ModifierCatalog.SpiderMode));
            var profile = CameraModifierPipeline.Build(c.Active, false);
            Assert.AreEqual(0f, profile.Vignette, "só os efeitos do túnel saíram");
            Assert.AreEqual(90f, profile.Roll, "a rotação da aranha continua");
        }

        [Test]
        public void OppositeCurvesStillReplaceEachOther()
        {
            var c = new ModifierController();
            Apply(c, ModifierCatalog.CurveRight);
            Apply(c, ModifierCatalog.CurveLeft);
            Assert.IsFalse(Has(c, ModifierCatalog.CurveRight));
            Assert.IsTrue(Has(c, ModifierCatalog.CurveLeft));
        }

        [Test]
        public void ConfiguredLimitAllowsFourEffects()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<FriendSlop.Core.MatchConfig>();
            Assert.That(config.MaxGameplayModifiersPerPlayer, Is.GreaterThanOrEqualTo(4));
            UnityEngine.Object.DestroyImmediate(config);
        }
    }
}
