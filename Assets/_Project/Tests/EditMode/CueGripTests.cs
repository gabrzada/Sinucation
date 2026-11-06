using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;
namespace FriendSlop.Tests
{
    public class CueGripTests
    {
        [TestCase(Archetype.Square, 1)] [TestCase(Archetype.Circle, 1)] [TestCase(Archetype.Triangle, 1)]
        [TestCase(Archetype.Square, -1)] [TestCase(Archetype.Circle, -1)] [TestCase(Archetype.Triangle, -1)]
        public void BridgeRemainsPlantedDuringFollowThrough(Archetype shape, int side)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(side * .78f, 0f);
            var direction = new Vector2(-side, side).normalized;
            var flat = new Vector3(direction.x, 0f, direction.y);
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("FollowBridgeActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                float lastPull = 0f, length = 0f;
                Vector3 anchor = Vector3.zero;
                for (int frame = 0; frame < 150; frame++)
                {
                    Vector3 tip;
                    if (frame < 120)
                    {
                        float pull = frame < 60 ? 0f : (frame - 60) * .3f / 59f;
                        lastPull = avatar.VisualCuePull(tip0, tilt, pull, 1f / 60f);
                        tip = tip0 - tilt * lastPull;
                        length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    }
                    else
                    {
                        // Match CueView's contact and follow-through, including the fixed cue length.
                        float elapsed = (frame - 120) / 60f;
                        float k = Mathf.Clamp01(elapsed / MatchDirector.ShotContactSeconds);
                        float push = k * k * (lastPull + .01f) + Mathf.Clamp01((elapsed - MatchDirector.ShotContactSeconds) / .12f) * .15f;
                        tip = tip0 - tilt * lastPull + tilt * push;
                    }
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    var groove = avatar.LeftHand.TransformPoint(new Vector3(-.0589854f, -.11728474f, -.00291428f));
                    if (frame == 60) anchor = groove;
                    if (frame >= 120)
                    {
                        Assert.That(avatar.LeftHand.Find("GloveBridge").gameObject.activeSelf, Is.True);
                        Assert.That(Vector3.Distance(groove, anchor), Is.LessThan(.005f), "the bridge should remain planted through the shot");
                        Assert.That(Vector3.ProjectOnPlane(groove - tip, tilt).magnitude, Is.LessThan(.006f), "the cue should remain in the bridge groove");
                    }
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [TestCase(Archetype.Square)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Triangle)]
        public void ImmediatePullDoesNotJumpWhileBridgePoseSettles(Archetype shape)
        {
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(-.78f, .39f);
            var direction = Vector2.down;
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("ImmediatePullActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var flat = new Vector3(direction.x, 0f, direction.y);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                float previous = 0f;
                for (int frame = 0; frame < 60; frame++)
                {
                    float visualPull = avatar.VisualCuePull(tip0, tilt, ShotController.MaxPull, 1f / 60f);
                    if (frame > 0) Assert.That(Mathf.Abs(visualPull - previous), Is.LessThan(.021f), "the cue should not jump as the bridge pose settles");
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip0 - tilt * visualPull, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    previous = visualPull;
                }
                Assert.That(previous, Is.GreaterThan(.02f), "the cue should still move after the bridge settles");
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [Test]
        public void RemoteCueWaitsForGrippingPose()
        {
            var holder = new GameObject("CueReadinessActor");
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", Archetype.Square, Color.cyan));
            try
            {
                var tip = new Vector3(.28f, .65f, .8f);
                var bridge = new Vector3(.28f, .655f, .62f);
                avatar.SetCue(tip, Vector3.forward, 1.1f, bridge);
                Assert.That(avatar.CueGripReady, Is.False);
                avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                Assert.That(avatar.CueGripReady, Is.False, "the cue should wait for the hand to settle");
                for (int i = 0; i < 90; i++)
                {
                    avatar.SetCue(tip, Vector3.forward, 1.1f, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                }
                Assert.That(avatar.CueGripReady, Is.True);
                avatar.Animate(1f / 60f, AvatarPose.Idle, 0f, false);
                Assert.That(avatar.CueGripReady, Is.False);
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [TestCase(Archetype.Square)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Triangle)]
        public void CueDoesNotPassThroughHeadDuringAiming(Archetype shape)
        {
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(-.9f, .45f);
            float angle = 225f * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("HeadClearanceActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var head = System.Array.Find(avatar.Head.GetComponentsInChildren<MeshFilter>(), f => f.name == "HeadShape");
                Assert.That(head, Is.Not.Null);
                var collider = head.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = head.sharedMesh;
                head.gameObject.layer = 31;
                var flat = new Vector3(direction.x, 0f, direction.y);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var ballWorld = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = ballWorld - tilt * (settings.BallRadius + .01f);
                var tip = tip0 - tilt * .2f;
                var bridge = ballWorld - flat * .18f; bridge.y = TableView.TopY + .035f;
                float length = 0f;
                for (int i = 0; i < 100; i++)
                {
                    length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                }
                Physics.SyncTransforms();
                Assert.That(collider.Raycast(new Ray(tip - tilt * length, tilt), out _, length), Is.False, "cue must not intersect the head");
                Assert.That(Physics.SphereCast(tip - tilt * length, .011f, tilt, out _, length, 1 << 31, QueryTriggerInteraction.Ignore), Is.False, "the cue thickness must also clear the head");
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [TestCase(Archetype.Square, -.78f, -.39f, 315f)]
        [TestCase(Archetype.Circle, 0f, 0f, 90f)]
        [TestCase(Archetype.Triangle, .78f, .39f, 135f)]
        public void BridgeHandStaysPlantedAsCuePullsAndReturns(Archetype shape, float x, float z, float degrees)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(x, z);
            float radians = degrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("PlantedBridgeActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var flat = new Vector3(direction.x, 0f, direction.y);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                var grooveLocal = new Vector3(-.0589854f, -.11728474f, -.00291428f);
                var glove = avatar.LeftHand.Find("GloveBridge");
                Vector3 planted = Vector3.zero;
                for (int frame = 0; frame < 210; frame++)
                {
                    float pull = frame < 90 ? 0f : frame < 150 ? (frame - 90) * .3f / 59f : (209 - frame) * .3f / 59f;
                    float visualPull = avatar.VisualCuePull(tip0, tilt, pull);
                    var tip = tip0 - tilt * visualPull;
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    var groove = avatar.LeftHand.TransformPoint(grooveLocal);
                    if (frame == 90) planted = groove;
                    if (frame > 90)
                    {
                        Assert.That(glove.gameObject.activeSelf, Is.True, "the bridge glove should remain visible");
                        Assert.That(Vector3.Distance(groove, planted), Is.LessThan(.005f), "the front hand should stay planted");
                        Assert.That(Vector3.ProjectOnPlane(groove - tip, tilt).magnitude, Is.LessThan(.006f), "the cue should slide through the glove");
                        Assert.That(Vector3.Dot(groove - tip, -tilt), Is.GreaterThanOrEqualTo(-.002f), "the hand should not pass beyond the cue tip");
                    }
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [TestCase(Archetype.Square)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Triangle)]
        public void BridgeHandRemainsOnCueThroughoutFullPull(Archetype shape)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(-.78f, -.39f);
            float angle = 315f * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("FullPullActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var flat = new Vector3(direction.x, 0f, direction.y);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                var grooveLocal = new Vector3(-.0589854f, -.11728474f, -.00291428f);
                var glove = avatar.LeftHand.Find("GloveBridge");
                Vector3 previous = Vector3.zero;
                for (int frame = 0; frame < 120; frame++)
                {
                    float pull = frame < 60 ? 0f : (frame - 60) * .3f / 59f;
                    var tip = tip0 - tilt * pull;
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    var groove = avatar.LeftHand.TransformPoint(grooveLocal);
                    if (frame > 60)
                    {
                        Assert.That(glove.gameObject.activeSelf, Is.True, "bridge glove should remain active throughout the pull");
                        Assert.That(Vector3.ProjectOnPlane(groove - tip, tilt).magnitude, Is.LessThan(.006f), "the cue must remain in the bridge groove");
                        Assert.That(Vector3.Distance(groove, previous), Is.LessThan(.012f), "bridge should not jump between frames");
                    }
                    previous = groove;
                }
                avatar.Animate(1f / 60f, AvatarPose.Idle, 0f, false);
                var shoulder = (Transform)typeof(AvatarVisual).GetField("_shoulderL", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(avatar);
                Assert.That(Vector3.Distance(shoulder.localPosition, new Vector3(-.2f, .34f, 0f)), Is.LessThan(.001f), "shoulder should return after aiming");
            }
            finally { Object.DestroyImmediate(holder); }
        }
        [TestCase(Archetype.Triangle,0f)] [TestCase(Archetype.Triangle,.2f)] [TestCase(Archetype.Triangle,-.12f)]
        [TestCase(Archetype.Circle,0f)] [TestCase(Archetype.Circle,.2f)] [TestCase(Archetype.Circle,-.12f)]
        [TestCase(Archetype.Square,0f)] [TestCase(Archetype.Square,.2f)] [TestCase(Archetype.Square,-.12f)]
        public void RightGloveWrapsTheCueDuringStroke(Archetype shape,float pull)
        {
            var holder=new GameObject("GripActor");
            var avatar=AvatarVisual.Create(holder.transform,new PlayerProfile("QA",shape,Color.cyan));
            try
            {
                var direction=Quaternion.AngleAxis(8f,Vector3.right)*Vector3.forward;
                var ball=new Vector3(ShotMath.StanceSide,TableView.TopY+.029f,.8f);
                var tip0=ball-direction*.04f;var tip=tip0-direction*pull;
                var bridge=ball-Vector3.forward*.18f;bridge.y=TableView.TopY+.035f;
                for(int i=0;i<120;i++)
                {
                    float length=Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition-tip0,-direction)+.2f+AvatarVisual.CueGripFromButt,.75f,2.4f);
                    avatar.SetCue(tip,direction,length,bridge);avatar.Animate(1f/60f,AvatarPose.Aiming,10f,false);
                }
                var fist=avatar.RightHand.Find("GloveFist");
                Assert.That(fist,Is.Not.Null);Assert.That(fist.gameObject.activeSelf,Is.True,"gripping hand should be closed");
                var channel=avatar.RightHand.TransformPoint(AvatarVisual.FistChannelLocal*AvatarVisual.FistScale);
                var lineDistance=Vector3.ProjectOnPlane(channel-tip,direction).magnitude;
                Assert.That(lineDistance,Is.LessThan(.006f),"cue should pass through the closed glove channel");
                Assert.That(Vector3.Angle(avatar.RightHand.forward,direction),Is.LessThan(8f),"glove channel should follow cue direction");
                var leftBridge=avatar.LeftHand.Find("GloveBridge");
                Assert.That(leftBridge,Is.Not.Null);Assert.That(leftBridge.gameObject.activeSelf,Is.True,"bridge hand should support the cue");
                var groove=avatar.LeftHand.TransformPoint(new Vector3(-.0589854f,-.11728474f,-.00291428f));
                Assert.That(Vector3.ProjectOnPlane(groove-tip,direction).magnitude,Is.LessThan(.01f),"cue should pass through the bridge groove");
                var sleeve=fist.Find("WristSleeve"); var joint=fist.Find("CueWristJoint");
                Assert.That(joint.gameObject.activeSelf,Is.True); Assert.That(sleeve.gameObject.activeSelf,Is.False);
                avatar.Animate(1f/60f,AvatarPose.Idle,0f,false);
                Assert.That(joint.gameObject.activeSelf,Is.False); Assert.That(sleeve.gameObject.activeSelf,Is.True); 
            }
            finally {Object.DestroyImmediate(holder);}
        }
    }
}






