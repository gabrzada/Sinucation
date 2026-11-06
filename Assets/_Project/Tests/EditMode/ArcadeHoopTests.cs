using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FriendSlop.Tests
{
    /// <summary>
    /// Cesta de arcade móvel: o aro move-se igual em todos os peers (função do relógio de rede), só conta o objecto que
    /// entra por cima a descer, cada lançamento pontua uma vez, só a autoridade decide e o prémio passa pela economia da
    /// partida (+1, com limite por ronda, nunca fora da partida, em pausa ou a eliminados).
    /// </summary>
    public class ArcadeHoopTests
    {
        readonly List<Object> _owned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            foreach (var o in _owned)
                if (o != null) Object.DestroyImmediate(o);
            _owned.Clear();
        }

        MatchDirector NewMatch(int players, int seed, bool bots = false, System.Action<MatchConfig> configure = null)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            config.RoundIntroSeconds = 0.1f;
            configure?.Invoke(config);
            var catalog = ModifierCatalog.CreateDefaults();
            _owned.Add(config);
            _owned.AddRange(catalog);
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < players; i++) profiles.Add(new PlayerProfile("P" + i, (Archetype)(i % 3), Color.white) { IsBot = bots });
            return new MatchDirector(config, catalog, profiles, seed);
        }

        /// <summary>Voo balístico relativo ao centro do aro, em passos de física de 20 ms; conta quantas vezes o RimTrack marca cesta.</summary>
        static int Fly(ref RimTrack track, Vector3 start, Vector3 velocity, float seconds, bool authority = true, float radius = 0.045f)
        {
            const float dt = 0.02f;
            int scores = 0;
            var p = start;
            var v = velocity;
            for (float t = 0f; t <= seconds; t += dt)
            {
                if (track.Step(p, v.y, radius, authority)) scores++;
                v += Physics.gravity * dt;
                p += v * dt;
            }
            return scores;
        }

        [Test]
        public void RimSwayIsTheSameFunctionOfNetworkTimeEverywhereAndMovesGently()
        {
            for (double t = -20.0; t < 40.0; t += 0.37)
            {
                float x = HoopRules.Offset(t);
                Assert.AreEqual(x, HoopRules.Offset(t), "o mesmo instante de rede dá a mesma posição");
                Assert.AreEqual(x, HoopRules.Offset(t + HoopRules.SwayPeriod), 1e-4f, "vai e volta com período fixo");
                Assert.LessOrEqual(Mathf.Abs(x), HoopRules.SwayAmplitude + 1e-5f);
            }
            float maxSpeed = 0f, min = 0f, max = 0f;
            for (double t = 0.0; t < HoopRules.SwayPeriod; t += 0.01)
            {
                maxSpeed = Mathf.Max(maxSpeed, Mathf.Abs(HoopRules.Offset(t + 0.01) - HoopRules.Offset(t)) / 0.01f);
                min = Mathf.Min(min, HoopRules.Offset(t));
                max = Mathf.Max(max, HoopRules.Offset(t));
            }
            Assert.Less(maxSpeed, 1f, "velocidade moderada (não enjoa)");
            Assert.Greater(maxSpeed, 0.5f, "mas o alvo não fica parado");
            Assert.Less(min, -0.95f * HoopRules.SwayAmplitude, "vai até à esquerda");
            Assert.Greater(max, 0.95f * HoopRules.SwayAmplitude, "e até à direita");
        }

        [Test]
        public void ObjectDroppingThroughTheRimScoresExactlyOncePerThrow()
        {
            var track = new RimTrack();
            Assert.AreEqual(1, Fly(ref track, new Vector3(0.05f, 0.6f, -0.02f), new Vector3(0f, 0f, 0f), 1.5f), "cai por dentro do aro: uma cesta");

            // O mesmo objecto ressalta por baixo, volta a subir pelo aro e cai outra vez: continua a ser o mesmo lançamento.
            Assert.AreEqual(0, Fly(ref track, new Vector3(0f, -0.3f, 0f), new Vector3(0f, 4f, 0f), 1.2f));
            Assert.IsTrue(track.Scored);
        }

        [Test]
        public void LobbedThrowComingDownIntoTheRimScores()
        {
            // Arco real: lançado 1 m abaixo do aro à velocidade do [G] (7,5 m/s pela câmara + 1,2 para cima), a olhar 47° acima.
            var track = new RimTrack();
            float angle = 47f * Mathf.Deg2Rad;
            var v = new Vector3(0f, 7.5f * Mathf.Sin(angle) + 1.2f, 7.5f * Mathf.Cos(angle));
            int best = 0;
            // Procura o alcance que acerta (o que interessa é que existe um arco que desce para dentro do aro).
            for (float d = 2.5f; d < 9f && best == 0; d += 0.05f)
            {
                track = new RimTrack();
                best = Fly(ref track, new Vector3(0f, -1f, -d), v, 2.5f);
            }
            Assert.AreEqual(1, best);
        }

        [Test]
        public void RisingObjectDoesNotScoreEvenWhenItFallsBackThroughTheRim()
        {
            var track = new RimTrack();
            // Vem de baixo da rede, sobe pelo aro e volta a cair por ele: nunca entrou por cima.
            Assert.AreEqual(0, Fly(ref track, new Vector3(0f, -0.5f, 0f), new Vector3(0f, 4.2f, 0f), 2f));
            Assert.IsTrue(track.CameFromBelow);
            Assert.IsFalse(track.Scored);
            Assert.AreEqual(RimCrossing.Up, HoopRules.Classify(new Vector3(0f, -0.01f, 0f), new Vector3(0f, 0.01f, 0f), 2f, 0.05f));
            Assert.AreEqual(RimCrossing.None, HoopRules.Classify(new Vector3(0f, 0.01f, 0f), new Vector3(0f, -0.01f, 0f), 2f, 0.05f),
                "a atravessar para baixo mas com velocidade a subir (teleporte/correcção) não conta");
        }

        [Test]
        public void MissesOutsideTheRimAndTeleportsDoNotScore()
        {
            var track = new RimTrack();
            Assert.AreEqual(0, Fly(ref track, new Vector3(0.32f, 0.6f, 0f), Vector3.zero, 1.5f), "por fora do aro");
            track = new RimTrack();
            Assert.AreEqual(0, Fly(ref track, new Vector3(0f, 0.6f, 0.23f), Vector3.zero, 1.5f, radius: 0.07f), "pato a raspar no ferro: o centro fica fora da folga");
            Assert.AreEqual(RimCrossing.None, HoopRules.Classify(new Vector3(0f, 1.5f, 0f), new Vector3(0f, -0.1f, 0f), -3f, 0.05f), "salto de mais de 1 m entre passos");
        }

        [Test]
        public void OnlyTheAuthorityDecidesAndOnlyItsLedgerCanPay()
        {
            var client = new RimTrack();
            Assert.AreEqual(0, Fly(ref client, new Vector3(0f, 0.6f, 0f), Vector3.zero, 1.5f, authority: false), "um cliente nunca marca cesta");
            Assert.IsFalse(client.Scored);

            // Os ids vêm do registo do anfitrião; o de um cliente está vazio e não reconhece lançamentos.
            var hostLedger = new HoopLedger();
            var clientLedger = new HoopLedger();
            int id = hostLedger.Register(7, 10.0);
            Assert.IsFalse(clientLedger.TryClaim(id, 10.5, out _));
            Assert.IsTrue(hostLedger.TryClaim(id, 10.5, out ulong thrower));
            Assert.AreEqual(7UL, thrower, "o prémio vai para quem fez o lançamento");
        }

        [Test]
        public void LedgerPaysEachThrowOnceAndForgetsOldOnes()
        {
            var ledger = new HoopLedger();
            int a = ledger.Register(1, 0.0);
            int b = ledger.Register(2, 0.5);
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(ledger.TryClaim(b, 1.0, out ulong who));
            Assert.AreEqual(2UL, who);
            Assert.IsFalse(ledger.TryClaim(b, 1.1, out _), "o mesmo lançamento não pontua duas vezes");
            Assert.IsFalse(ledger.TryClaim(999, 1.1, out _), "lançamento desconhecido");
            Assert.IsFalse(ledger.TryClaim(a, HoopLedger.ThrowLifetime + 0.5, out _), "objecto que já desapareceu");
            int c = ledger.Register(1, 100.0);
            Assert.AreEqual(1, ledger.OpenThrows, "lançamentos velhos são esquecidos");
            Assert.IsTrue(ledger.TryClaim(c, 101.0, out _), "um lançamento novo do mesmo jogador volta a poder pontuar");
        }

        [Test]
        public void BasketCreditsOneCoinThroughTheMatchEconomy()
        {
            var d = NewMatch(2, 3);
            Assert.AreEqual(0, d.CreditBasket(0), "antes de a partida começar (sala de espera) não há carteira");
            d.Start();
            d.Tick(0.2f);
            var reasons = new List<string>();
            d.Economy.BalanceChanged += (p, delta, reason) => reasons.Add(p.Id + ":" + delta + ":" + reason);
            Assert.AreEqual(1, d.CreditBasket(1));
            Assert.AreEqual(1, d.GetPlayer(1).Coins);
            Assert.AreEqual(0, d.GetPlayer(0).Coins, "só quem lançou recebe");
            CollectionAssert.AreEqual(new[] { "1:1:Cesta" }, reasons, "o mesmo caminho dos prémios (BalanceChanged → evento Coins → HUD)");
            Assert.AreEqual(1, d.GetPlayer(1).CoinsEarned);
            Assert.AreEqual(0, d.CreditBasket(5), "jogador inexistente (espectador)");
        }

        [Test]
        public void BasketPaysNothingWhilePausedOrToEliminatedPlayers()
        {
            var d = NewMatch(3, 4);
            d.Start();
            d.Tick(0.2f);
            d.SetPaused(true, "teste");
            Assert.AreEqual(0, d.CreditBasket(0));
            d.SetPaused(false, "teste");
            Assert.AreEqual(1, d.CreditBasket(0));
            d.GetPlayer(2).Eliminated = true;
            Assert.AreEqual(0, d.CreditBasket(2));
            Assert.AreEqual(0, d.GetPlayer(2).Coins);
        }

        [Test]
        public void BasketRewardIsCappedPerRoundAndTheCapResetsNextRound()
        {
            var d = NewMatch(3, 11, bots: true, configure: c => c.HoopRoundCap = 3);
            d.Start();
            d.Tick(0.2f);
            int total = 0;
            for (int i = 0; i < 6; i++) total += d.CreditBasket(0);
            Assert.AreEqual(3, total, "no máximo HoopRoundCap Dinheiros de cestas por ronda");
            Assert.AreEqual(3, d.GetPlayer(0).BasketsThisRound);

            var brain = new BotBrain(11);
            int round = d.RoundIndex;
            for (int i = 0; i < 400000 && d.RoundIndex == round && d.Phase != MatchPhase.MatchOver; i++)
            {
                brain.Update(d, 0.05f);
                d.Tick(0.05f);
            }
            Assume.That(d.RoundIndex > round && !d.GetPlayer(0).Eliminated, "precisa de uma ronda nova com o jogador 0 em jogo");
            Assert.AreEqual(0, d.GetPlayer(0).BasketsThisRound);
            Assert.AreEqual(1, d.CreditBasket(0), "ronda nova, limite novo");
            Assert.AreEqual(4, d.GetPlayer(0).Baskets);
        }
    }
}
