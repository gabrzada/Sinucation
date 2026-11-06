using System.Linq;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class JukeboxModelTests
    {
        [Test]
        public void NewCabinetKeepsInteractionAudioAndAnimatedLights()
        {
            var root = new GameObject("JukeboxTest");
            try
            {
                var jukebox = root.AddComponent<JukeboxStation>();
                jukebox.Build(null);
                Assert.That(root.transform.Find("JukeboxCabinet"), Is.Not.Null);
                Assert.That(root.transform.Find("JukeboxRecords"), Is.Not.Null);
                Assert.That(root.transform.Find("Window").GetComponent<Renderer>().enabled, Is.True);
                Assert.That(root.transform.Find("Grill"), Is.Not.Null);
                Assert.That(root.transform.Cast<Transform>().Count(t => t.name == "Button"), Is.EqualTo(6));
                Assert.That(root.transform.Cast<Transform>().Count(t => t.name == "Light"), Is.EqualTo(12));
                Assert.That(root.GetComponent<BoxCollider>().size, Is.EqualTo(new Vector3(1f,2f,.7f)));
                Assert.That(jukebox.FocusPoint, Is.EqualTo(new Vector3(0f,1.2f,-.3f)));
                Assert.That(jukebox.Zooms, Is.False);
                Assert.That(root.GetComponent<AudioSource>(), Is.Not.Null);
                Assert.That(root.GetComponent<OnlineMusicPlayer>(), Is.Not.Null);
                Assert.That(InteractionSystem.All.Contains(jukebox), Is.True);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
