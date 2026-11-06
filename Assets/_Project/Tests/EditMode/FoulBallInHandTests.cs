using System.Collections.Generic;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Regra: se o jogador da vez encaçapa a branca ou toca primeiro na bola errada, o jogador seguinte escolhe onde
    /// pôr a branca (bola na mão em qualquer sítio da mesa, não só na cabeceira).
    /// </summary>
    public class FoulBallInHandTests
    {
        static MatchDirector ReadyMatch(int seed)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            config.RoundIntroSeconds = 0.1f;
            config.ShotResultSeconds = 0.1f;
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < 3; i++) profiles.Add(new PlayerProfile("P" + i, (Archetype)i, Color.white));
            var d = new MatchDirector(config, ModifierCatalog.CreateDefaults(), profiles, seed);
            d.Start();
            for (int i = 0; i < 10 && d.Phase != MatchPhase.AwaitShot; i++) d.Tick(0.1f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            return d;
        }

        static void Arrange(MatchDirector d, Vector2 cue, params (int number, Vector2 position)[] balls)
        {
            var state = new List<BallState> { new BallState { Number = 0, Position = cue } };
            foreach (var b in balls) state.Add(new BallState { Number = b.number, Position = b.position });
            d.Sim.LoadState(state, 9);
        }

        static void ShootAndSettle(MatchDirector d, Vector2 direction, float power)
        {
            int shooter = d.CurrentPlayerId;
            Assert.AreEqual(CommandResult.Accepted, d.SubmitShot(new ShotRequest { PlayerId = shooter, ShotSequence = d.ShotSequence, Direction = direction, Power = power }));
            for (int i = 0; i < 4000 && d.Phase != MatchPhase.AwaitShot; i++) d.Tick(0.02f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
        }

        static void AssertNextPlayerChoosesAnywhere(MatchDirector d, int shooter, FoulType expected)
        {
            Assert.AreEqual(expected, d.LastOutcome.Foul);
            Assert.AreNotEqual(shooter, d.CurrentPlayerId, "a falta passa a vez");
            Assert.IsTrue(d.BallInHand, "o jogador seguinte fica com bola na mão");
            Assert.IsFalse(d.BallInHandKitchenOnly, "pode pôr a branca em qualquer sítio, não só na cabeceira");
            Assert.IsFalse(d.Sim.Cue.Pocketed, "a branca volta à mesa");

            // Escolhe um sítio no lado dos pés da mesa (longe da cabeceira) e a colocação é aceite.
            var spot = d.Sim.FindFreeSpot(new Vector2(d.Sim.Table.HalfLength * 0.6f, 0.1f), 0, false);
            Assert.Greater(spot.x, d.Sim.Table.HeadStringX);
            Assert.IsTrue(d.RequestPlaceCueBall(d.CurrentPlayerId, spot));
            Assert.AreEqual(spot, d.Sim.Cue.Position);
            // Só o jogador da vez escolhe.
            Assert.IsFalse(d.RequestPlaceCueBall(shooter, spot + Vector2.up * 0.1f));
        }

        [Test]
        public void HittingTheWrongBallFirstGivesTheNextPlayerBallInHandAnywhere()
        {
            var d = ReadyMatch(31);
            int shooter = d.CurrentPlayerId;
            // A bola 1 (a legal) está longe; a 2 está em linha recta com a branca.
            Arrange(d, new Vector2(-0.6f, -0.3f), (1, new Vector2(0.6f, 0.35f)), (2, new Vector2(0.2f, -0.3f)), (9, new Vector2(0.9f, 0f)));
            ShootAndSettle(d, Vector2.right, 0.35f);
            AssertNextPlayerChoosesAnywhere(d, shooter, FoulType.WrongBallFirst);
        }

        [Test]
        public void PocketingTheCueBallGivesTheNextPlayerBallInHandAnywhere()
        {
            var d = ReadyMatch(32);
            int shooter = d.CurrentPlayerId;
            // Branca perto de uma caçapa de canto, apontada directamente a ela, sem bolas no caminho.
            var pocket = d.Sim.Table.Pockets[0].Center;
            var inward = -pocket.normalized;
            var cue = pocket + inward * 0.25f;
            Arrange(d, cue, (1, new Vector2(0.3f, 0.3f)), (2, new Vector2(0.5f, -0.2f)), (9, new Vector2(0.9f, 0f)));
            ShootAndSettle(d, (pocket - cue).normalized, 0.4f);
            AssertNextPlayerChoosesAnywhere(d, shooter, FoulType.Scratch);
        }
    }
}
