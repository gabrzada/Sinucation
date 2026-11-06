using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Tests
{
    public class CueSleeveLifecycleTests
    {
        [TestCase(Archetype.Square)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Triangle)]
        public void SleeveFollowsVisibilityAndReleasesMeshWhenAppearanceChanges(Archetype shape)
        {
            var holder = new GameObject("SleeveLifecycle");
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            void Aim()
            {
                for (int frame = 0; frame < 120; frame++)
                {
                    avatar.SetCue(new Vector3(.2f, .92f, .65f), Vector3.forward, 1.2f, new Vector3(.2f, .9f, .5f));
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                }
            }
            Mesh mesh = null;
            try
            {
                avatar.SetFirstPersonHidden(true);
                Aim();
                var sleeve = avatar.transform.Find("CueSleeveCurve");
                Assert.That(sleeve, Is.Not.Null);
                var renderer = sleeve.GetComponent<Renderer>();
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly), "new sleeve must respect an already hidden local avatar");
                avatar.SetFirstPersonHidden(false);
                Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.On));
                mesh = sleeve.GetComponent<MeshFilter>().sharedMesh;
                foreach (var vertex in mesh.vertices)
                    Assert.That(float.IsNaN(vertex.sqrMagnitude) || float.IsInfinity(vertex.sqrMagnitude), Is.False);
                avatar.Apply(new PlayerProfile("QA", shape, Color.red));
                Assert.That(mesh == null, Is.True, "appearance rebuild must release the previous sleeve mesh");
                Assert.That(avatar.transform.Find("CueSleeveCurve"), Is.Null, "appearance rebuild must remove the previous sleeve");
                Aim();
                mesh = avatar.transform.Find("CueSleeveCurve").GetComponent<MeshFilter>().sharedMesh;
                // EditMode does not exercise the normal runtime destruction loop.
                typeof(AvatarVisual).GetMethod("OnDestroy", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(avatar, null);
            }
            finally { Object.DestroyImmediate(holder); }
            Assert.That(mesh == null, Is.True, "the avatar destruction callback must release its generated sleeve mesh");
        }
    }
}


