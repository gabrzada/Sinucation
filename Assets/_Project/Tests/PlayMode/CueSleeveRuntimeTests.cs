using System.Collections;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace FriendSlop.Tests
{
    public class CueSleeveRuntimeTests
    {
        [UnityTest]
        public IEnumerator SleeveSurvivesPoseAndAppearanceChangesAndIsReleasedWithAvatar()
        {
            foreach (var shape in new[] { Archetype.Square, Archetype.Circle, Archetype.Triangle })
            {
                var holder = new GameObject("RuntimeSleeveActor");
                Mesh rebuiltMesh = null;
                try
                {
                    var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
                    avatar.SetFirstPersonHidden(true);
                    for (int frame = 0; frame < 90; frame++)
                    {
                        avatar.SetCue(new Vector3(.2f, .92f, .65f), Vector3.forward, 1.2f, new Vector3(.2f, .9f, .5f));
                        avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                        yield return null;
                    }
                    var sleeve = avatar.transform.Find("CueSleeveCurve");
                    Assert.That(sleeve, Is.Not.Null, shape.ToString());
                    var renderer = sleeve.GetComponent<Renderer>();
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));
                    var oldMesh = sleeve.GetComponent<MeshFilter>().sharedMesh;
                    avatar.Animate(1f / 60f, AvatarPose.Idle, 0f, false);
                    Assert.That(sleeve.gameObject.activeSelf, Is.False);
                    avatar.Apply(new PlayerProfile("QA", shape, Color.red));
                    yield return null;
                    Assert.That(oldMesh == null, Is.True, "runtime appearance change must release old mesh");
                    Assert.That(sleeve == null, Is.True, "runtime appearance change must remove old sleeve");
                    for (int frame = 0; frame < 90; frame++)
                    {
                        avatar.SetCue(new Vector3(.2f, .92f, .65f), Vector3.forward, 1.2f, new Vector3(.2f, .9f, .5f));
                        avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                        yield return null;
                    }
                    sleeve = avatar.transform.Find("CueSleeveCurve");
                    Assert.That(sleeve, Is.Not.Null);
                    rebuiltMesh = sleeve.GetComponent<MeshFilter>().sharedMesh;
                }
                finally { Object.Destroy(holder); }
                yield return null;
                yield return null;
                Assert.That(rebuiltMesh == null, Is.True, "runtime avatar destruction must release generated sleeve mesh: " + shape);
            }
        }
    }
}
