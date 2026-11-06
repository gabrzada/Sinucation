using System.Collections.Generic;
using System.IO;
using FriendSlop.Core;
using FriendSlop.Core.Modifiers;
using FriendSlop.Match;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>Regressões da revisão de 27/09: bots, cel shading opcional, upscalers, jukebox online.</summary>
    public class ReviewRegressionTests
    {
        static MatchDirector BotMatch(int players, int seed)
        {
            var config = ScriptableObject.CreateInstance<MatchConfig>();
            config.RoundIntroSeconds = 0.1f;
            var profiles = new List<PlayerProfile>();
            for (int i = 0; i < players; i++) profiles.Add(new PlayerProfile("Bot" + i, (Archetype)(i % 3), Color.white) { IsBot = true });
            return new MatchDirector(config, ModifierCatalog.CreateDefaults(), profiles, seed);
        }

        [Test]
        public void BotWaitsForItsAvatarThenAimsAndWindsUpBeforeTheShot()
        {
            var d = BotMatch(3, 41);
            bool ready = false;
            var brain = new BotBrain(41) { ReadyToShoot = id => ready };
            d.Start();
            for (int i = 0; i < 50 && d.Phase != MatchPhase.AwaitShot; i++) d.Tick(0.1f);
            Assert.AreEqual(MatchPhase.AwaitShot, d.Phase);
            int sequence = d.ShotSequence;

            // A caminhar para o lugar: não puxa o taco nem tacar.
            for (int i = 0; i < 80; i++) { brain.Update(d, 0.1f); d.Tick(0.1f); }
            Assert.AreEqual(sequence, d.ShotSequence, "não pode tacar antes de chegar ao lugar");
            Assert.AreEqual(0f, brain.Aim.Pull, 1e-4f, "o taco só recua depois de chegar");

            ready = true;
            float firstPull = -1f, elapsed = 0f;
            while (d.ShotSequence == sequence && elapsed < 6f)
            {
                brain.Update(d, 0.05f);
                if (firstPull < 0f && brain.Aim.Pull > 0.01f) firstPull = elapsed;
                d.Tick(0.05f);
                elapsed += 0.05f;
            }
            Assert.AreEqual(sequence + 1, d.ShotSequence, "depois de chegar tem de tacar");
            Assert.Greater(firstPull, 0.3f, "assenta na pose antes de começar a puxar");
            Assert.Less(elapsed, 4f, "a tacada sai pouco depois de chegar (sem pausa longa)");
            Assert.AreEqual(MatchPhase.ShotWindup, d.Phase, "a força só é aplicada no contacto do taco");
        }

        [Test]
        public void BotStillShootsBeforeTimeoutWhenItsPathIsBlocked()
        {
            var d = BotMatch(3, 43);
            var brain = new BotBrain(43) { ReadyToShoot = id => false };
            d.Start();
            for (int i = 0; i < 20000 && d.ShotSequence < 3; i++) { brain.Update(d, 0.1f); d.Tick(0.1f); }
            Assert.GreaterOrEqual(d.ShotSequence, 3);
            Assert.AreEqual(0, d.Telemetry.Timeouts, "o bot nunca deixa o relógio do turno acabar");
        }

        [Test]
        public void LegacyGraphicsSettingsTurnCelShadingOff()
        {
            // Antes o cel suave era o padrão e ia sempre gravado: quem nunca o escolheu passa a ver o visual normal.
            var legacy = GraphicsOptions.FromJson("{\"CelShading\":1,\"Shadows\":2}");
            Assert.AreEqual(0, legacy.CelShading);
            Assert.AreEqual(2, legacy.Shadows);
            var chosen = GraphicsOptions.FromJson(new GraphicsOptions { CelShading = 2 }.ToJson());
            Assert.AreEqual(2, chosen.CelShading, "a escolha explícita (formato novo) mantém-se");
            Assert.AreEqual(0, new GraphicsOptions().CelShading);
        }

        [Test]
        public void LegacyRenderScaleBecomesTheUpscalerMode()
        {
            Assert.AreEqual(3, GraphicsOptions.FromJson("{\"RenderScale\":0.5,\"Upscaler\":3}").UpscalerQuality);
            Assert.AreEqual(1, GraphicsOptions.FromJson("{\"RenderScale\":1.0,\"Upscaler\":3}").UpscalerQuality);
        }

        [Test]
        public void OnlyVendorUpscalersUseTheirOwnQualityModes()
        {
            for (int i = 0; i < UpscalerController.Ids.Length; i++)
                Assert.AreEqual(i >= 3, UpscalerController.UsesQualityMode(i), UpscalerController.Names[i]);
            Assert.AreEqual("DLAA (nativo)", UpscalerController.QualityName(3, 0));
#if ENABLE_UPSCALER_FRAMEWORK
            var modes = new HashSet<int>();
            for (int m = 0; m < UpscalerController.QualityNames.Length; m++) modes.Add((int)XeSSOptions.QualityForMode(m));
            Assert.AreEqual(UpscalerController.QualityNames.Length, modes.Count, "cada modo do menu tem de mudar a resolução interna do XeSS");
#endif
        }

        [Test]
        public void FullOnlineQueueFitsInOneNetworkMessage()
        {
            var state = new OnlineMusicState { Current = LongTrack(0) };
            for (int i = 1; i <= OnlineMusicState.MaxQueue + 5; i++) state.Queue.Add(LongTrack(i));
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                state.Write(w);
                w.Flush();
                Assert.Less(ms.Length, 12 * 1024, "a fila (pior caso, só acentos) tem de caber com folga no limite de 16 KB por mensagem");
                ms.Position = 0;
                var copy = new OnlineMusicState();
                copy.Read(new BinaryReader(ms));
                Assert.AreEqual(OnlineMusicState.MaxQueue, copy.Queue.Count);
                Assert.AreEqual(state.Queue[3].VideoId, copy.Queue[3].VideoId);
            }
        }

        static OnlineTrack LongTrack(int i) => new OnlineTrack
        {
            VideoId = "abcdefghij" + (char)('a' + i % 26),
            Title = new string('Ã', 200),
            Channel = new string('ç', 120),
            AddedByName = new string('é', 60),
            Duration = 180 + i,
            AddedBy = (ulong)i
        };

        [Test]
        public void OnlineTrackAdvancesAfterItsDurationOrALongSafetyLimit()
        {
            var state = new OnlineMusicState { Current = new OnlineTrack { VideoId = "dQw4w9WgXcQ", Duration = 100 }, StartedAt = 0 };
            Assert.IsFalse(state.Finished(101));
            Assert.IsTrue(state.Finished(103));
            state.Paused = true;
            Assert.IsFalse(state.Finished(500), "em pausa nunca acaba");
            state.Paused = false;
            state.Current.Duration = 0;
            Assert.IsFalse(state.Finished(600));
            Assert.IsTrue(state.Finished(OnlineMusicState.UnknownDurationLimit + 1));
        }

        [Test]
        public void PlaylistLinksAreDetectedAndVideoInsidePlaylistAddsOnlyTheVideo()
        {
            Assert.IsTrue(YouTubeLink.TryParse("https://www.youtube.com/playlist?list=PL1234567890ABCDEF", out var v, out var l));
            Assert.IsTrue(YouTubeLink.IsPlaylistRequest(v, l));
            Assert.IsTrue(YouTubeLink.TryParse("https://www.youtube.com/watch?v=dQw4w9WgXcQ&list=PL1234567890ABCDEF", out v, out l));
            Assert.IsFalse(YouTubeLink.IsPlaylistRequest(v, l));
            Assert.IsTrue(YouTubeLink.TryParse("youtu.be/dQw4w9WgXcQ", out v, out _), "link colado sem https://");
            Assert.AreEqual("dQw4w9WgXcQ", v);
            Assert.IsFalse(YouTubeLink.TryParse("http://www.youtube.com/watch?v=dQw4w9WgXcQ", out _, out _), "só HTTPS");
        }

        [Test]
        public void RadioPauseFreezesThePositionAndSurvivesTheNetworkCodec()
        {
            var music = new FriendSlop.Net.MusicView { Track = 2, StartedAt = 100, Paused = true, PausedAt = 42.5 };
            Assert.AreEqual(42.5, music.RadioPosition(500), 1e-6, "em pausa a posição fica parada");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                music.Write(w);
                w.Flush();
                ms.Position = 0;
                var copy = new FriendSlop.Net.MusicView();
                copy.Read(new BinaryReader(ms));
                Assert.IsTrue(copy.Paused);
                Assert.AreEqual(42.5, copy.PausedAt, 1e-6);
                copy.Paused = false;
                Assert.AreEqual(400, copy.RadioPosition(500), 1e-6);
            }
        }

        [Test]
        public void PerPlayerCountIncludesTheCurrentTrack()
        {
            var state = new OnlineMusicState { Current = new OnlineTrack { AddedBy = 5 } };
            state.Queue.Add(new OnlineTrack { AddedBy = 5 });
            state.Queue.Add(new OnlineTrack { AddedBy = 6 });
            Assert.AreEqual(2, state.CountBy(5));
            Assert.AreEqual(1, state.CountBy(6));
        }
    }
}
