using System.Reflection;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class RearGripTests
    {
        [TestCase(Archetype.Square, 1)] [TestCase(Archetype.Circle, 1)] [TestCase(Archetype.Triangle, 1)]
        [TestCase(Archetype.Square, -1)] [TestCase(Archetype.Circle, -1)] [TestCase(Archetype.Triangle, -1)]
        public void ElbowDoesNotTravelExcessivelyDuringBackswing(Archetype shape, int side)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(side * .78f, side * .39f);
            var direction = new Vector2(side, 0f);
            var flat = new Vector3(direction.x, 0f, direction.y);
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("ElbowTravelActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var elbow = (Transform)typeof(AvatarVisual).GetField("_elbowR", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(avatar);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                Vector3 plantedElbow = Vector3.zero;
                for (int frame = 0; frame < 150; frame++)
                {
                    float pull = frame < 90 ? 0f : (frame - 90) * .3f / 59f;
                    float visualPull = avatar.VisualCuePull(tip0, tilt, pull, 1f / 60f);
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip0 - tilt * visualPull, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    if (frame == 90) plantedElbow = elbow.position;
                    else if (frame > 90) Assert.That(Vector3.Distance(plantedElbow, elbow.position), Is.LessThan(.145f), "the elbow should stay nearer its starting position");
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [TestCase(Archetype.Square, -1)] [TestCase(Archetype.Circle, -1)] [TestCase(Archetype.Triangle, -1)]
        [TestCase(Archetype.Square, 1)] [TestCase(Archetype.Circle, 1)] [TestCase(Archetype.Triangle, 1)]
        public void ForearmApproachesGloveDuringBackswing(Archetype shape, int side)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(side * .45f, -side * .45f);
            var direction = new Vector2(side, side).normalized;
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("RearGripActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var elbow = (Transform)typeof(AvatarVisual).GetField("_elbowR", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(avatar);
                var flat = new Vector3(direction.x, 0f, direction.y);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                Quaternion previous = Quaternion.identity;
                Vector3 previousElbow = Vector3.zero;
                for (int frame = 0; frame < 120; frame++)
                {
                    float pull = frame < 60 ? 0f : (frame - 60) * .3f / 59f;
                    float visualPull = avatar.VisualCuePull(tip0, tilt, pull);
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    var tip = tip0 - tilt * visualPull;
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    if (frame >= 60)
                    {
                        var wrist = avatar.RightHand;
                        float alignment = Vector3.Dot((elbow.position - wrist.position).normalized, wrist.up);
                        Assert.That(alignment, Is.GreaterThan(-.15f), "the forearm should approach the cuff rather than cross the fingers");
                        var channel = wrist.TransformPoint(AvatarVisual.FistChannelLocal * AvatarVisual.FistScale);
                        Assert.That(Vector3.ProjectOnPlane(channel - tip, tilt).magnitude, Is.LessThan(.01f), "the cue should stay inside the glove");
                        if (frame > 60)
                        {
                            Assert.That(Quaternion.Angle(previous, wrist.rotation), Is.LessThan(5f), "the wrist should not flip during the pull");
                            Assert.That(Vector3.Distance(previousElbow, elbow.position), Is.LessThan(.015f), "the elbow should not jump between frames");
                        }
                        previous = wrist.rotation;
                        previousElbow = elbow.position;
                    }
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
