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
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + AvatarVisual.CueGripBehindShoulder + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip0 - tilt * visualPull, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    if (frame == 90) plantedElbow = elbow.position;
                    else if (frame > 90) Assert.That(Vector3.Distance(plantedElbow, elbow.position), Is.LessThan(.145f), "the elbow should stay nearer its starting position");
                }
            }
            finally { Object.DestroyImmediate(holder); }
        }

        /// <summary>
        /// Dobra entre o punho da luva (eixo +Y da mão) e a direcção pulso → cotovelo, do repouso ao fim do recuo.
        /// Antes da tacada pendular o caso de referência (branca em -0,90; 0,22, mira a 45°) media 74° → 97°.
        /// </summary>
        [TestCase(Archetype.Square, -.90f, .22f, 45f)] [TestCase(Archetype.Circle, -.90f, .22f, 45f)] [TestCase(Archetype.Triangle, -.90f, .22f, 45f)]
        [TestCase(Archetype.Square, .78f, .39f, 0f)] [TestCase(Archetype.Square, .45f, -.45f, 45f)] [TestCase(Archetype.Circle, 0f, 0f, -90f)]
        [TestCase(Archetype.Triangle, -.6f, -.3f, 160f)] [TestCase(Archetype.Square, 1f, .4f, 200f)]
        public void RearWristStaysNearlyStraightThroughStroke(Archetype shape, float x, float y, float aimDegrees)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(x, y);
            float rad = aimDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            var flat = new Vector3(direction.x, 0f, direction.y);
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("RearWristActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var elbow = (Transform)typeof(AvatarVisual).GetField("_elbowR", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(avatar);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                float atRest = 0f, worst = 0f;
                for (int frame = 0; frame < 150; frame++)
                {
                    float pull = frame < 90 ? 0f : Mathf.Min(ShotController.MaxPull, (frame - 90) * ShotController.MaxPull / 40f);
                    float visualPull = avatar.VisualCuePull(tip0, tilt, pull, 1f / 60f);
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + AvatarVisual.CueGripBehindShoulder + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    var tip = tip0 - tilt * visualPull;
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    if (frame < 89) continue;
                    var wrist = avatar.RightHand;
                    float bend = Vector3.Angle(wrist.up, elbow.position - wrist.position);
                    if (frame == 89) atRest = bend;
                    worst = Mathf.Max(worst, bend);
                    var channel = wrist.TransformPoint(AvatarVisual.FistChannelLocal * AvatarVisual.FistScale);
                    Assert.That(Vector3.ProjectOnPlane(channel - tip, tilt).magnitude, Is.LessThan(.01f), "the cue should stay inside the glove");
                }
                TestContext.WriteLine($"rear wrist bend: rest {atRest:F1}°, worst {worst:F1}°");
                Assert.That(atRest, Is.LessThan(25f), "the rear wrist should be close to straight at address");
                Assert.That(worst, Is.LessThan(40f), "the rear wrist should not fold during the backswing");
            }
            finally { Object.DestroyImmediate(holder); }
        }

        /// <summary>O antebraço da ponte chega à luva pela direcção para que ela foi modelada (por trás, da esquerda, de cima).</summary>
        [TestCase(Archetype.Square, -.90f, .22f, 45f)] [TestCase(Archetype.Circle, .78f, .39f, 0f)] [TestCase(Archetype.Triangle, .45f, -.45f, 45f)]
        [TestCase(Archetype.Square, 0f, 0f, -90f)] [TestCase(Archetype.Circle, -.6f, -.3f, 160f)] [TestCase(Archetype.Square, 1f, .4f, 200f)]
        public void BridgeForearmMeetsTheGloveCuff(Archetype shape, float x, float y, float aimDegrees)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(x, y);
            float rad = aimDegrees * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            var flat = new Vector3(direction.x, 0f, direction.y);
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("BridgeArmActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var f = BindingFlags.Instance | BindingFlags.NonPublic;
                var elbow = (Transform)typeof(AvatarVisual).GetField("_elbowL", f).GetValue(avatar);
                var bridgeGlove = (Transform)typeof(AvatarVisual).GetField("_bridgeL", f).GetValue(avatar);
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip0 = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                var designed = Quaternion.LookRotation(flat, Vector3.up) * AvatarVisual.BridgeForearmLocal;
                float worst = 0f; int samples = 0;
                for (int frame = 0; frame < 150; frame++)
                {
                    float pull = frame < 90 ? 0f : Mathf.Min(ShotController.MaxPull, (frame - 90) * ShotController.MaxPull / 40f);
                    float visualPull = avatar.VisualCuePull(tip0, tilt, pull, 1f / 60f);
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + AvatarVisual.CueGripBehindShoulder + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                    avatar.SetCue(tip0 - tilt * visualPull, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                    if (frame < 89 || bridgeGlove == null || !bridgeGlove.gameObject.activeSelf) continue;
                    worst = Mathf.Max(worst, Vector3.Angle(avatar.LeftHand.position - elbow.position, designed));
                    samples++;
                }
                TestContext.WriteLine($"bridge forearm vs design: worst {worst:F1}° over {samples} frames");
                Assume.That(samples, Is.GreaterThan(0), "the bridge glove should be in use");
                // Before aligning the elbow these cases measured 31°–72°. Where the twisted torso puts the left
                // shoulder level with or ahead of the bridge the arm still folds (~45°–58°).
                Assert.That(worst, Is.LessThan(60f), "the forearm should reach the bridge glove the way it was modelled");
            }
            finally { Object.DestroyImmediate(holder); }
        }

        [TestCase(Archetype.Square)] [TestCase(Archetype.Circle)] [TestCase(Archetype.Triangle)]
        public void HandsReturnToRestAfterAiming(Archetype shape)
        {
            Random.InitState(2026);
            var settings = new BilliardsPhysicsSettings();
            var table = new TableGeometry(settings);
            var ball = new Vector2(.45f, -.45f);
            var direction = new Vector2(1f, 1f).normalized;
            var flat = new Vector3(direction.x, 0f, direction.y);
            var stand = ShotMath.StandBehind(ball, direction, table);
            var holder = new GameObject("HandRestActor");
            holder.transform.SetPositionAndRotation(new Vector3(stand.x, 0f, stand.y), Quaternion.Euler(0f, ShotMath.WorldYaw(direction), 0f));
            var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA", shape, Color.cyan));
            try
            {
                var tilt = Quaternion.AngleAxis(ShotMath.CueTilt(ball, direction, table, settings.BallRadius), Vector3.Cross(Vector3.up, flat)) * flat;
                var worldBall = TableView.ToWorld(ball, settings.BallRadius);
                var tip = worldBall - tilt * (settings.BallRadius + .01f);
                var bridge = worldBall - flat * .18f; bridge.y = TableView.TopY + .035f;
                float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip, -tilt) + AvatarVisual.CueGripBehindShoulder + AvatarVisual.CueGripFromButt, .75f, 2.4f);
                for (int frame = 0; frame < 60; frame++)
                {
                    avatar.SetCue(tip, tilt, length, bridge);
                    avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                }
                Assume.That(Quaternion.Angle(avatar.RightHand.localRotation, Quaternion.identity), Is.GreaterThan(10f), "the grip should rotate the hand while aiming");
                // Stop publishing the cue (shot taken) and stand idle for a while.
                avatar.SetCue(tip, tilt, length, bridge);
                typeof(AvatarVisual).GetField("_ikSetTime", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(avatar, -10f);
                for (int frame = 0; frame < 30; frame++) avatar.Animate(1f / 60f, AvatarPose.Idle, 0f, false);
                Assert.That(Quaternion.Angle(avatar.RightHand.localRotation, Quaternion.identity), Is.LessThan(1f), "the rear glove should go back to its rest pose");
                Assert.That(Quaternion.Angle(avatar.LeftHand.localRotation, Quaternion.identity), Is.LessThan(1f), "the bridge glove should go back to its rest pose");
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
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -tilt) + AvatarVisual.CueGripBehindShoulder + AvatarVisual.CueGripFromButt, .75f, 2.4f);
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
