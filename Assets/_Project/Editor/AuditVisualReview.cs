using System;
using System.IO;
using System.Linq;
using FriendSlop.Core;
using FriendSlop.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.EditorTools
{
    public static class AuditVisualReview
    {
        public static void CaptureAndBuild()
        {
            Capture();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            var report = BuildPipeline.BuildPlayer(scenes, "Builds/Audit/FriendSlop.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Audit build failed: " + report.summary.result);
        }

        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor.");
            string dir = "Blender/audit/cosmetics";
            Directory.CreateDirectory(dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f,.58f,.65f);
            RenderSettings.fog = false;
            var light = new GameObject("Key").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 2.2f;
            light.transform.rotation = Quaternion.Euler(35,-35,0);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .8f;
            fill.transform.rotation = Quaternion.Euler(20,150,0);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = 6.25f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.78f,.80f,.83f);
            cam.nearClipPlane = .05f; cam.farClipPlane = 30f;
            cam.transform.position = new Vector3(3.25f,6.0f,12);
            cam.transform.rotation = Quaternion.Euler(0,180,0);
            var rt = new RenderTexture(1800,2800,24); cam.targetTexture = rt;
            foreach (Archetype shape in new[] { Archetype.Triangle, Archetype.Circle, Archetype.Square })
            foreach (AvatarPose pose in new[] { AvatarPose.Idle, AvatarPose.Aiming, AvatarPose.Risk, AvatarPose.Celebrate })
            {
                var group = new GameObject("Combination grid");
                for (int hat = 0; hat < CosmeticIds.HatCount; hat++)
                for (int accessory = 0; accessory < CosmeticIds.AccessoryCount; accessory++)
                {
                    var holder = new GameObject("H" + hat + " A" + accessory);
                    holder.transform.SetParent(group.transform);
                    holder.transform.position = new Vector3((5-accessory)*1.3f, (5-hat)*2.05f, 0);
                    holder.transform.rotation = Quaternion.Euler(0,pose == AvatarPose.Aiming ? -65 : -25,0);
                    UnityEngine.Random.InitState(2718);
                    var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("QA",shape,ArtPalette.Heads[(int)shape]) { HatId=hat,AccessoryId=accessory });
                    var direction = holder.transform.TransformDirection(Quaternion.AngleAxis(8,Vector3.right)*Vector3.forward);
                    var ball = holder.transform.TransformPoint(new Vector3(ShotMath.StanceSide,TableView.TopY+.029f,.8f));
                    var tip = ball-direction*.04f;
                    var bridge = ball-holder.transform.forward*.18f;
                    bridge.y = holder.transform.position.y+TableView.TopY+.035f;
                    float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition-tip,-direction)+.2f+AvatarVisual.CueGripFromButt,.75f,2.4f);
                    for (int frame=0;frame<60;frame++)
                    {
                        if(pose==AvatarPose.Aiming) avatar.SetCue(tip,direction,length,bridge);
                        avatar.Animate(1f/60,pose,8,false);
                    }
                    if(pose==AvatarPose.Aiming)
                    {
                        var cue=VisualFactory.Primitive(PrimitiveType.Cylinder,"Cue",group.transform,tip-direction*length*.5f,
                            new Vector3(.018f,length*.5f,.018f),VisualFactory.Lit(new Color(.6f,.35f,.12f)));
                        cue.transform.rotation=Quaternion.LookRotation(direction)*Quaternion.Euler(90,0,0);
                    }
                }
                cam.Render(); cam.Render();
                RenderTexture.active=rt;
                var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                File.WriteAllBytes(Path.Combine(dir,shape+"-"+pose+".png"),image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(group);
            }
            cam.targetTexture=null; RenderTexture.active=null; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            File.WriteAllText(Path.Combine(dir,"README.txt"),"Rows: hat IDs 0..5 from top to bottom. Columns: accessory IDs 0..5 from left to right. Four poses, three archetypes. Camera faces +Z characters from +Z.\n");
            Debug.Log("[AuditVisualReview] Cosmetic captures complete.");
        }
    }
}
