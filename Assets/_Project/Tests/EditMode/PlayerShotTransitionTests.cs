using System.Reflection;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;
namespace FriendSlop.Tests
{
    public class PlayerShotTransitionTests
    {
        [Test]
        public void StancesDoNotRequireTableDepenetrationWhenControlReturns()
        {
            var table=new TableGeometry(new BilliardsPhysicsSettings());
            for(int x=-4;x<=4;x++) for(int z=-4;z<=4;z++) for(int degrees=0;degrees<360;degrees+=5)
            {
                var ball=new Vector2(x*table.HalfLength/5f,z*table.HalfWidth/5f);
                float a=degrees*Mathf.Deg2Rad;
                var stand=ShotMath.StandBehind(ball,new Vector2(Mathf.Cos(a),Mathf.Sin(a)),table);
                var position=new Vector3(stand.x,0,stand.y);
                var safe=RoomBuilder.OutsideTable(position,table.HalfLength,table.HalfWidth,.28f);
                Assert.That(Vector3.Distance(position,safe),Is.LessThan(.001f),$"ball={ball}, degrees={degrees}, stand={stand}");
            }
        }

        [Test]
        public void LongShotCueClearsRailWithoutUnnecessaryElevation()
        {
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var cue = new Vector2(-table.HalfLength * .8f, 0f);
            var direction = Vector2.left;
            float degrees = ShotMath.CueTilt(cue, direction, table, settings.BallRadius);
            float distanceToRail = table.HalfLength + cue.x * Mathf.Sign(direction.x);
            float cueHeightAtRail = settings.BallRadius + distanceToRail * Mathf.Tan(degrees * Mathf.Deg2Rad);
            Assert.That(degrees, Is.LessThan(4f), "a long shot should not be raised unnecessarily");
            Assert.That(cueHeightAtRail, Is.GreaterThanOrEqualTo(.0759f), "the cue must clear the rail");
        }
        [Test]
        public void VisualLeanReturnsAfterAimingWithoutMovingController()
        {
            var holder = new GameObject("LeanActor");
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", Archetype.Square, Color.cyan));
            try
            {
                Vector3 start = holder.transform.position;
                var rig = avatar.transform.Find("Rig");
                for (int i = 0; i < 90; i++)
                {
                    avatar.SetCue(new Vector3(.28f, .65f, .8f), Vector3.forward, 1.1f, new Vector3(.28f, .655f, .62f));
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                }
                Assert.That(rig.localPosition.z, Is.InRange(.14f, .151f));
                Assert.That(holder.transform.position, Is.EqualTo(start));
                for (int i = 0; i < 90; i++) avatar.Animate(1f / 60f, AvatarPose.Idle, 0f, false);
                Assert.That(rig.localPosition.magnitude, Is.LessThan(.001f));
                Assert.That(holder.transform.position, Is.EqualTo(start));
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [TestCase(Archetype.Triangle)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Square)]
        public void StanceTeleportReplantsFeetNearTheNewBody(Archetype archetype)
        {
            var holder=new GameObject("TransitionActor");
            var avatar=AvatarVisual.Create(holder.transform,new PlayerProfile("QA",archetype,Color.cyan));
            try
            {
                for(int i=0;i<90;i++)avatar.Animate(1f/60f,AvatarPose.Idle,0,false);
                holder.transform.position += new Vector3(.65f,0,0);
                avatar.Animate(1f/60f,AvatarPose.Aiming,0,false);
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                foreach(var name in new[]{"_footL","_footR"})
                {
                    var foot=typeof(AvatarVisual).GetField(name,flags).GetValue(avatar);
                    var planted=(Vector3)foot.GetType().GetField("Planted").GetValue(foot);
                    var delta=planted-avatar.transform.position;delta.y=0;
                    Assert.That(delta.magnitude,Is.LessThan(.3f),name+" stayed at the old position");
                }
            }
            finally{Object.DestroyImmediate(holder);}
        }
    }
}


