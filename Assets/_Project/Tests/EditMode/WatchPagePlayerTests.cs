using System.Text.RegularExpressions;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    /// <summary>Jukebox na página oficial do YouTube (navegador embutido): URL, erros, navegação permitida e máscara.</summary>
    public class WatchPagePlayerTests
    {
        [Test]
        public void WatchUrlStartsAtTheRoomPositionAndRejectsBadIds()
        {
            Assert.That(YouTubeWatchPage.Url("dQw4w9WgXcQ", 0), Is.EqualTo("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
            Assert.That(YouTubeWatchPage.Url("dQw4w9WgXcQ", 73.9), Is.EqualTo("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=73s"));
            Assert.That(YouTubeWatchPage.Url("dQw4w9WgXcQ", double.NaN), Is.EqualTo("https://www.youtube.com/watch?v=dQw4w9WgXcQ"));
            Assert.IsNull(YouTubeWatchPage.Url("abc", 10));
            Assert.IsNull(YouTubeWatchPage.Url("dQw4w9WgX&'", 10), "nada de caracteres fora do formato de ID");
        }

        [TestCase(150, true)]
        [TestCase(101, true)]
        [TestCase(YouTubeWatchPage.LoginRequired, true)]
        [TestCase(YouTubeWatchPage.Unplayable, true)]
        [TestCase(YouTubeWatchPage.Removed, false)]
        [TestCase(YouTubeWatchPage.NeedsConfirmation, false)]
        public void OnlyErrorsNobodyCanPlaySearchForAnotherVersion(int code, bool search) =>
            Assert.That(YouTubeWatchPage.ShouldSearchAlternative(code), Is.EqualTo(search));

        [TestCase("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=30s", false)]
        [TestCase("https://m.youtube.com/watch?v=dQw4w9WgXcQ", false)]
        [TestCase("https://consent.youtube.com/m?continue=https://www.youtube.com/watch", false)]
        [TestCase("about:blank", false)]
        [TestCase("https://accounts.google.com/ServiceLogin", true)]
        [TestCase("https://www.youtube.com.evil.test/watch?v=dQw4w9WgXcQ", true)]
        [TestCase("http://www.youtube.com/watch?v=dQw4w9WgXcQ", true)]
        [TestCase("https://advertiser.example/landing", true)]
        public void NavigationOutsideYouTubeIsHooked(string url, bool hooked) =>
            Assert.That(Regex.IsMatch(url, YouTubeWatchPage.HookPattern, RegexOptions.IgnoreCase), Is.EqualTo(hooked));

        [Test]
        public void OnlyHttpsOpensInTheSystemBrowser()
        {
            Assert.IsTrue(YouTubeWatchPage.IsSafeExternal("https://accounts.google.com/ServiceLogin"));
            Assert.IsFalse(YouTubeWatchPage.IsSafeExternal("javascript:alert(1)"));
            Assert.IsFalse(YouTubeWatchPage.IsSafeExternal("file:///C:/Windows/notepad.exe"));
            Assert.IsFalse(YouTubeWatchPage.IsSafeExternal("https://user@evil.test/"));
        }

        [Test]
        public void RoomWaitsForAdsAndEveryoneStartsTogether()
        {
            var state = new OnlineMusicState { Current = new OnlineTrack { VideoId = "dQw4w9WgXcQ", Duration = 200 }, StartedAt = 100 };
            Assert.IsTrue(state.SetAdHold(true, 102), "começa a esperar nos primeiros segundos");
            Assert.That(state.Position(102), Is.EqualTo(0), "volta ao início da faixa");
            Assert.That(state.Position(130), Is.EqualTo(0), "relógio parado enquanto há anúncios");
            Assert.IsFalse(state.Finished(100000), "a espera nunca acaba a faixa");
            Assert.IsFalse(state.SetAdHold(true, 131), "sem mudança, sem nova revisão");
            Assert.IsTrue(state.SetAdHold(false, 140));
            Assert.That(state.Position(150), Is.EqualTo(10), "depois todos andam juntos a partir do início");

            using var stream = new System.IO.MemoryStream(); using var writer = new System.IO.BinaryWriter(stream);
            state.SetAdHold(true, 150);
            state.Write(writer); writer.Flush(); stream.Position = 0;
            var copy = new OnlineMusicState(); copy.Read(new System.IO.BinaryReader(stream));
            Assert.IsTrue(copy.AdHold, "a espera chega aos clientes");
            Assert.That(copy.Position(999), Is.EqualTo(state.Position(999)));
        }

        [Test]
        public void MaskCropsThePlayerAndKeepsItsProportion()
        {
            // Player a toda a largura sob a barra do YouTube (56 px) numa página de 656 × 576.
            var crop = new Rect(0f, 56f / 576f, 641f / 656f, 369f / 576f);
            var uv = OnlineMusicPlayer.CropUv(crop);
            Assert.That(uv.x, Is.EqualTo(0f));
            Assert.That(uv.yMax, Is.EqualTo(56f / 576f).Within(1e-5f), "topo do player no topo do desenho (textura de cima para baixo)");
            Assert.That(uv.yMin, Is.EqualTo(425f / 576f).Within(1e-5f));

            var fitted = OnlineMusicPlayer.Fit(new Rect(10f, 20f, 800f, 450f), 641f / 369f);
            Assert.That(fitted.height, Is.EqualTo(450f).Within(0.01f));
            Assert.That(fitted.width / fitted.height, Is.EqualTo(641f / 369f).Within(1e-3f));
            Assert.That(fitted.center.x, Is.EqualTo(410f).Within(0.01f), "centrado, com barras dos lados");

            var whole = OnlineMusicPlayer.Fit(new Rect(0f, 0f, 800f, 450f), 656f / 576f);
            Assert.That(whole.height, Is.EqualTo(450f).Within(0.01f), "página inteira (escolha de cookies) cabe toda");
        }
    }
}
