using System.IO;
using FriendSlop.Core;
using NUnit.Framework;

namespace FriendSlop.Tests
{
    public class OnlineMusicTests
    {
        [TestCase("https://youtu.be/dQw4w9WgXcQ")]
        [TestCase("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=4")]
        [TestCase("https://youtube.com/playlist?list=PL123456789012345")]
        public void AcceptsOfficialVideoAndPlaylistLinks(string url) => Assert.IsTrue(YouTubeLink.TryParse(url, out _, out _));

        [TestCase("https://youtube.com.evil.test/watch?v=dQw4w9WgXcQ")]
        [TestCase("https://youtube.com@evil.test/watch?v=dQw4w9WgXcQ")]
        [TestCase("file:///C:/secret.txt")]
        [TestCase("http://127.0.0.1:8888/")]
        [TestCase("https://www.youtube.com/watch?v=%22%3Balert(1)")]
        [TestCase("https://www.youtube.com/")]
        public void RejectsInvalidOrUntrustedLinks(string url) => Assert.IsFalse(YouTubeLink.TryParse(url, out _, out _));

        [Test]
        public void SharedQueueRestoresForLateJoinAndPauseStopsTheClock()
        {
            var state = new OnlineMusicState { Current = new OnlineTrack { VideoId = "dQw4w9WgXcQ", Title = "Example", Duration = 200, AddedBy = 7 },
                StartedAt = 10, Offset = 30, Paused = true, HostOnly = true, Revision = 8 };
            state.Queue.Add(new OnlineTrack { VideoId = "abcdefghijk", AddedBy = 9, Channel = "Channel" });
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            state.Write(writer); writer.Flush(); stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var copy = new OnlineMusicState(); copy.Read(reader);
            Assert.That(copy.Current.VideoId, Is.EqualTo(state.Current.VideoId));
            Assert.That(copy.Queue[0].AddedBy, Is.EqualTo(9));
            Assert.That(copy.HostOnly, Is.True);
            Assert.That(copy.Position(100), Is.EqualTo(30));
            copy.Paused = false; copy.StartedAt = 100;
            Assert.That(copy.Position(107), Is.EqualTo(37));
        }

        [Test]
        public void JukeboxAndDvdShareOneQueueAndKeepWhereEachItemWasChosen()
        {
            var state = new OnlineMusicState { Current = new OnlineTrack { VideoId = "aqz-KE-bpKQ", Device = OnlineTrack.FromJukebox }, StartedAt = 5 };
            state.Queue.Add(new OnlineTrack { VideoId = "eRsGyueVLvQ", Device = OnlineTrack.FromDvd, AddedBy = 2 });
            state.Queue.Add(new OnlineTrack { VideoId = "abcdefghijk", Device = OnlineTrack.FromJukebox, AddedBy = 3 });
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            state.Write(writer); writer.Flush(); stream.Position = 0;
            using var reader = new BinaryReader(stream);
            var copy = new OnlineMusicState(); copy.Read(reader);
            Assert.That(copy.Current.Device, Is.EqualTo(OnlineTrack.FromJukebox));
            Assert.That(copy.Queue[0].Device, Is.EqualTo(OnlineTrack.FromDvd));
            Assert.That(copy.Queue[1].Device, Is.EqualTo(OnlineTrack.FromJukebox));
            Assert.That(OnlineTrack.DeviceName(copy.Queue[0].Device), Is.EqualTo("DVD"));
            Assert.That(stream.Position, Is.EqualTo(stream.Length), "a mensagem é lida até ao fim");
        }
    }
}
