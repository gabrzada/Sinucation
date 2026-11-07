using System.Reflection;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Match;
using FriendSlop.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace FriendSlop.Tests {
public class RearFollowThroughTests {
 [TestCase(Archetype.Square, 1)] [TestCase(Archetype.Circle, 1)] [TestCase(Archetype.Triangle, 1)]
 [TestCase(Archetype.Square, -1)] [TestCase(Archetype.Circle, -1)] [TestCase(Archetype.Triangle, -1)]
 public void GripAndArmStayContinuousThroughStroke(Archetype shape,int side) {
  Random.InitState(2026); var settings=new BilliardsPhysicsSettings(); var table=new TableGeometry(settings);
  var ball=new Vector2(side*.78f,0f); var direction=new Vector2(-side,side).normalized; var flat=new Vector3(direction.x,0,direction.y);
  var stand=ShotMath.StandBehind(ball,direction,table); var holder=new GameObject("RearFollowActor"); holder.transform.SetPositionAndRotation(new Vector3(stand.x,0,stand.y),Quaternion.Euler(0,ShotMath.WorldYaw(direction),0));
  var avatar=AvatarVisual.Create(holder.transform,new PlayerProfile("QA",shape,Color.cyan));
  try {
   var elbow=(Transform)typeof(AvatarVisual).GetField("_elbowR",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(avatar);
   var tilt=Quaternion.AngleAxis(ShotMath.CueTilt(ball,direction,table,settings.BallRadius),Vector3.Cross(Vector3.up,flat))*flat;
   var worldBall=TableView.ToWorld(ball,settings.BallRadius); var tip0=worldBall-tilt*(settings.BallRadius+.01f); var bridge=worldBall-flat*.18f;bridge.y=TableView.TopY+.035f;
   float lastPull=0,length=0; Quaternion previous=Quaternion.identity; Vector3 previousElbow=Vector3.zero, previousTip=Vector3.zero;
   for(int frame=0;frame<150;frame++) {
    Vector3 tip;
    if(frame<120) { float pull=frame<60?0:(frame-60)*.3f/59f;lastPull=avatar.VisualCuePull(tip0,tilt,pull,1f/60f);tip=tip0-tilt*lastPull;length=Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition-tip0,-tilt)+AvatarVisual.CueGripBehindShoulder+AvatarVisual.CueGripFromButt,.75f,2.4f); }
    else { float elapsed=(frame-120)/60f;float k=Mathf.Clamp01(elapsed/MatchDirector.ShotContactSeconds);float push=k*k*(lastPull+.01f)+Mathf.Clamp01((elapsed-MatchDirector.ShotContactSeconds)/.12f)*.15f;tip=tip0-tilt*lastPull+tilt*push; }
    avatar.SetCue(tip,tilt,length,bridge);avatar.Animate(1f/60f,AvatarPose.Aiming,10f,false);
    if(frame>=120) {
     var wrist=avatar.RightHand;var channel=wrist.TransformPoint(AvatarVisual.FistChannelLocal*AvatarVisual.FistScale);
     Assert.That(Vector3.ProjectOnPlane(channel-tip,tilt).magnitude,Is.LessThan(.01f),"the cue should stay inside the rear glove during follow-through");
     Assert.That(Quaternion.Angle(previous,wrist.rotation),Is.LessThan(5f),"the rear wrist should not flip during follow-through");
     Assert.That(Vector3.Distance(previousElbow,elbow.position),Is.LessThan(.015f + 1.5f * Vector3.Distance(previousTip,tip)),"the rear elbow should not jump disproportionately to cue movement, frame " + frame);
    }
    previous=avatar.RightHand.rotation; previousElbow=elbow.position; previousTip=tip;
   }
  } finally { Object.DestroyImmediate(holder); }
 }
}}
