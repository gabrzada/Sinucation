using System.Linq;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class GameAuditTests
    {
        static Transform Joint(AvatarVisual avatar, string name) => avatar.GetComponentsInChildren<Transform>().First(t => t.name == name);
        static PlayerProfile Profile(Archetype a, int hat = 0, int accessory = 0) => new PlayerProfile("QA", a, Color.cyan) { HatId = hat, AccessoryId = accessory };

        [TestCase(Archetype.Triangle)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Square)]
        public void ElevatedAvatarKeepsSameLocalPose(Archetype a)
        {
            var ground = new GameObject("GroundActor");
            var balcony = new GameObject("BalconyActor");
            balcony.transform.position = Vector3.up * 2.96f;
            Random.InitState(52); var low = AvatarVisual.Create(ground.transform, Profile(a));
            Random.InitState(52); var high = AvatarVisual.Create(balcony.transform, Profile(a));
            try
            {
                for (int i = 0; i < 90; i++) { low.Animate(1f/60, AvatarPose.Idle, 0, false); high.Animate(1f/60, AvatarPose.Idle, 0, false); }
                Assert.That(Vector3.Distance(Joint(low,"Hips").localPosition, Joint(high,"Hips").localPosition), Is.LessThan(.001f));
                Assert.That(Joint(high,"Ankle").position.y, Is.InRange(3.04f,3.09f));
            }
            finally { Object.DestroyImmediate(ground); Object.DestroyImmediate(balcony); }
        }

        [TestCase(Archetype.Triangle)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Square)]
        public void EveryCosmeticCombinationHasSingleSlotsAndFiniteAnimatedMeshes(Archetype a)
        {
            var holder = new GameObject("CosmeticActor");
            var avatar = AvatarVisual.Create(holder.transform, Profile(a));
            try
            {
                for(int hat=0;hat<CosmeticIds.HatCount;hat++)
                for(int accessory=0;accessory<CosmeticIds.AccessoryCount;accessory++)
                {
                    avatar.Apply(Profile(a,hat,accessory));
                    var hierarchy=avatar.GetComponentsInChildren<Transform>();
                    Assert.That(hierarchy.Count(t=>t.name=="Rig"),Is.EqualTo(1));
                    Assert.That(hierarchy.Count(t=>t.name=="HeadwearSlot"),Is.EqualTo(1));
                    Assert.That(hierarchy.Count(t=>t.name=="AccessorySlot"),Is.EqualTo(1));
                    foreach(var pose in new[]{AvatarPose.Idle,AvatarPose.Aiming,AvatarPose.Risk,AvatarPose.Celebrate})
                    {
                        for(int frame=0;frame<20;frame++) avatar.Animate(1f/60,pose,8,false);
                        foreach(var renderer in avatar.GetComponentsInChildren<MeshRenderer>())
                        {
                            var b=renderer.bounds;
                            Assert.That(float.IsNaN(b.center.x)||float.IsNaN(b.size.y),Is.False);
                            Assert.That(b.size.magnitude,Is.LessThan(4f),a+" "+hat+" "+accessory+" "+renderer.name);
                            Assert.That(renderer.sharedMaterials.All(m=>m!=null),Is.True);
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [TestCase(Archetype.Triangle)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Square)]
        public void GlassesHaveConnectedTemplesThatFollowHead(Archetype shape)
        {
            var holder = new GameObject("GlassesActor");
            var avatar = AvatarVisual.Create(holder.transform, Profile(shape, 0, 1));
            try
            {
                var hinges = avatar.Head.GetComponentsInChildren<Transform>().Where(t => t.name == "GlassesHinge").ToArray();
                var arms = avatar.Head.GetComponentsInChildren<Transform>().Where(t => t.name == "GlassesArm").ToArray();
                Assert.That(hinges.Length, Is.EqualTo(2));
                Assert.That(arms.Length, Is.EqualTo(4));
                foreach (var hinge in hinges)
                    Assert.That(arms.Any(arm => Vector3.Distance(arm.position, hinge.position) < .001f), Is.True, "each hinge must join a temple");
                var attachment = avatar.Head.InverseTransformPoint(hinges[0].position);
                for (int i = 0; i < 60; i++) avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                Assert.That(Vector3.Distance(avatar.Head.InverseTransformPoint(hinges[0].position), attachment), Is.LessThan(.001f), "the glasses must move with the head");
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [TestCase(Archetype.Triangle)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Square)]
        public void CelebrationOverridesEmoteThenReturnsToIdle(Archetype a)
        {
            var holder=new GameObject("CelebrationActor");
            var avatar=AvatarVisual.Create(holder.transform,Profile(a));
            try
            {
                avatar.PlayEmote(2);
                for(int i=0;i<60;i++) avatar.Animate(1f/60,AvatarPose.Celebrate,0,false);
                float raised=avatar.RightHand.position.y;
                Assert.That(raised,Is.GreaterThan(Joint(avatar,"ShoulderR").position.y));
                for(int i=0;i<180;i++) avatar.Animate(1f/60,AvatarPose.Celebrate,0,false);
                Assert.That(avatar.RightHand.position.y,Is.LessThan(Joint(avatar,"ShoulderR").position.y));
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Test]
        public void PartyHatBandsStandClearOfConeSurface()
        {
            var model = FsModels.Get("accessories", "PartyHat");
            Assert.That(model, Is.Not.Null);
            int ivory = System.Array.IndexOf(model.Materials, "ivory");
            Assert.That(ivory, Is.GreaterThanOrEqualTo(0));
            var vertices = model.Mesh.vertices;
            var bandVertices = model.Mesh.GetIndices(ivory).Select(i => vertices[i]).ToArray();
            foreach (float height in new[] { .06f, .09f, .16f, .19f, .26f, .28f })
            {
                var atHeight = bandVertices.Where(v => Mathf.Abs(v.y - height) < .0001f).ToArray();
                Assert.That(atHeight.Length, Is.GreaterThan(0), "missing band at " + height);
                float radius = atHeight.Max(v => new Vector2(v.x, v.z).magnitude);
                float coneRadius = .145f * (.34f - height) / (.34f - .012f);
                Assert.That(radius - coneRadius, Is.GreaterThan(.0025f), "band should not flicker against the cone");
            }
        }
        [Test]
        public void HatCacheIncludesForwardOffset()
        {
            var a=HatFit.Fit(Archetype.Circle,HatFit.Cap,.47f,.31f,-6,0);
            var b=HatFit.Fit(Archetype.Circle,HatFit.Cap,.47f,.31f,-6,.025f);
            Assert.That(b.Position.z-a.Position.z,Is.EqualTo(.025f).Within(.0001f));
        }
    }
}


