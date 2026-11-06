using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests
{
    public class RailSupportTests
    {
        [TestCase(Archetype.Square, -1)] [TestCase(Archetype.Circle, -1)] [TestCase(Archetype.Triangle, -1)]
        [TestCase(Archetype.Square, 1)] [TestCase(Archetype.Circle, 1)] [TestCase(Archetype.Triangle, 1)]
        public void SteepShotKeepsBridgeNearRail(Archetype shape, int side)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(side * .45f, -side * .45f);
            var direction = new Vector2(side, side).normalized;
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("RailSupportActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var flat = new Vector3(direction.x, 0f, direction.y);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f;
                bridge.y = TableView.TopY + .035f;
                for (int i = 0; i < 120; i++)
                {
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip, -tilt) + .2f + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                }
                var glove = avatar.LeftHand.Find("GloveBridge");
                Assert.That(glove.gameObject.activeSelf, Is.True);
                float lowest = float.MaxValue;
                foreach (var mesh in glove.GetComponentsInChildren<MeshFilter>())
                    foreach (var vertex in mesh.sharedMesh.vertices)
                        lowest = Mathf.Min(lowest, mesh.transform.TransformPoint(vertex).y);
                float railTop = TableView.TopY + .06f;
                Assert.That(lowest, Is.InRange(railTop - .015f, railTop + .015f), "the supporting glove should be close to the rail");
                var groove = avatar.LeftHand.TransformPoint(new Vector3(-.0589854f, -.11728474f, -.00291428f));
                Assert.That(Vector3.ProjectOnPlane(groove - tip, tilt).magnitude, Is.LessThan(.006f));
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
