using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using FriendSlop.Core;
using FriendSlop.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.EditorTools
{
    // Batch-only review of the real runtime meshes and poses, without changing game scenes.
    public static class CharacterReview
    {
        [Serializable] public class HeadArchive { public List<HeadPart> parts = new List<HeadPart>(); }
        [Serializable] public class HeadPart
        {
            public string archetype, name;
            public Vector3[] vertices, normals;
            public List<Surface> surfaces = new List<Surface>();
        }
        [Serializable] public class Surface { public int[] triangles; public Color color; }

        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Run in a separate batch editor.");
            string dir = "Blender/review/after";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-characterReview") dir = args[i + 1];
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.65f);
            var light = new GameObject("ReviewKey").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.2f;
            light.transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            var fill = new GameObject("ReviewFill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.8f;
            fill.transform.rotation = Quaternion.Euler(20f, 150f, 0f);
            var cam = new GameObject("ReviewCamera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.84f, 0.81f, 0.74f);
            cam.orthographic = true;
            cam.orthographicSize = 0.88f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 30f;
            var rt = new RenderTexture(900, 1000, 24);
            cam.targetTexture = rt;
            var report = new StringBuilder();
            var headArchive = new HeadArchive();
            var archetypes = new[] { Archetype.Triangle, Archetype.Circle, Archetype.Square };
            for (int a = 0; a < archetypes.Length; a++)
            {
                UnityEngine.Random.InitState(2718);
                var holder = new GameObject("ReviewActor");
                var avatar = AvatarVisual.Create(holder.transform, new PlayerProfile("Review", archetypes[a], ArtPalette.Heads[a]) { HatId = 0 });
                ArchiveHead(avatar, archetypes[a], headArchive);
                foreach (float yaw in new[] { 0f, 35f, 180f })
                    Save(cam, rt, dir, archetypes[a] + "_" + yaw, new Vector3(0, 0.77f, 0), yaw, 7f);
                if (a == 2)
                {
                    var cue = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    cue.GetComponent<Renderer>().sharedMaterial = VisualFactory.Lit(new Color(0.6f, 0.35f, 0.12f));
                    var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    table.transform.position = new Vector3(0, TableView.TopY * 0.5f, 1.33f);
                    table.transform.localScale = new Vector3(1.2f, TableView.TopY, 2.2f);
                    table.GetComponent<Renderer>().sharedMaterial = VisualFactory.Lit(ArtPalette.Felt);
                    var dirCue = Quaternion.AngleAxis(8f, Vector3.right) * Vector3.forward;
                    var ball = new Vector3(ShotMath.StanceSide, TableView.TopY + 0.029f, 0.8f);
                    var tip0 = ball - dirCue * 0.04f;
                    var bridge = ball - Vector3.forward * 0.18f;
                    bridge.y = TableView.TopY + 0.035f;
                    for (int f = 0; f < 100; f++)
                    {
                        float pull = f < 60 ? 0f : f < 80 ? 0.2f : -0.12f;
                        var tip = tip0 - dirCue * pull;
                        float length = Mathf.Clamp(Vector3.Dot(avatar.RightShoulderPosition - tip0, -dirCue) + AvatarVisual.CueGripBehindShoulder + AvatarVisual.CueGripFromButt, 0.75f, 2.4f);
                        avatar.SetCue(tip, dirCue, length, bridge);
                        avatar.Animate(1f / 60f, AvatarPose.Aiming, 10f, false);
                        cue.transform.localScale = new Vector3(0.018f, length * 0.5f, 0.018f);
                        cue.transform.position = tip - dirCue * (length * 0.5f);
                        cue.transform.rotation = Quaternion.LookRotation(dirCue) * Quaternion.Euler(90f, 0, 0);
                        if (f == 59 || f == 79 || f == 99)
                        {
                            Save(cam, rt, dir, "aim_" + f, new Vector3(0, 0.55f, 0.2f), 65f, 18f);
                            foreach (var t in avatar.GetComponentsInChildren<Transform>())
                                if (t.GetComponent<Renderer>() == null)
                                    report.AppendLine(f + " " + t.name + " " + t.position.ToString("F6") + " " + t.rotation.ToString("F6"));
                        }
                    }
                    UnityEngine.Object.DestroyImmediate(table);
                    UnityEngine.Object.DestroyImmediate(cue);
                }
                UnityEngine.Object.DestroyImmediate(holder);
            }
            File.WriteAllText(Path.Combine(dir, "pose-transforms.txt"), report.ToString());
            File.WriteAllText(Path.Combine(dir, "head-meshes.json"), JsonUtility.ToJson(headArchive));
            // Full silhouettes at a common scale, using the actual runtime head and hat fit.
            var actors = new List<GameObject>();
            for (int a = 0; a < archetypes.Length; a++)
            {
                var actor = new GameObject("Lineup" + archetypes[a]);
                actor.transform.position = new Vector3((a - 1) * 0.9f, 0, 0);
                actor.transform.rotation = Quaternion.Euler(0, -15f, 0);
                var avatar = AvatarVisual.Create(actor.transform, new PlayerProfile("Review", archetypes[a], ArtPalette.Heads[a]) { HatId = 0 });
                foreach (var t in avatar.GetComponentsInChildren<Transform>())
                {
                    if (t.name == "ShoulderL") t.localRotation = Quaternion.Euler(0, 0, -9);
                    if (t.name == "ShoulderR") t.localRotation = Quaternion.Euler(0, 0, 9);
                }
                actors.Add(actor);
            }
            cam.orthographicSize = 1.0f;
            var wide = new RenderTexture(1800, 1000, 24);
            cam.targetTexture = wide;
            Save(cam, wide, dir, "lineup-unity", new Vector3(0, 0.82f, 0), 0, 5);
            cam.targetTexture = null;
            wide.Release();
            UnityEngine.Object.DestroyImmediate(wide);
            foreach (var actor in actors) UnityEngine.Object.DestroyImmediate(actor);
            cam.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log("[CharacterReview] Captured " + dir);
        }

        static void ArchiveHead(AvatarVisual avatar, Archetype archetype, HeadArchive archive)
        {
            foreach (var filter in avatar.Head.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = filter.sharedMesh;
                var matrix = avatar.Head.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var normalMatrix = matrix.inverse.transpose;
                var p = new HeadPart { archetype = archetype.ToString().ToLowerInvariant(), name = filter.name,
                    vertices = mesh.vertices, normals = mesh.normals };
                for (int i = 0; i < p.vertices.Length; i++)
                {
                    p.vertices[i] = matrix.MultiplyPoint3x4(p.vertices[i]);
                    p.normals[i] = normalMatrix.MultiplyVector(p.normals[i]).normalized;
                }
                var materials = filter.GetComponent<Renderer>().sharedMaterials;
                for (int i = 0; i < mesh.subMeshCount; i++)
                    p.surfaces.Add(new Surface { triangles = mesh.GetTriangles(i), color = materials[i].GetColor("_BaseColor") });
                archive.parts.Add(p);
            }
        }

        static void Save(Camera cam, RenderTexture rt, string dir, string name, Vector3 target, float yaw, float pitch)
        {
            cam.transform.position = target + Quaternion.Euler(-pitch, yaw, 0f) * new Vector3(0, 0, 4f);
            cam.transform.LookAt(target);
            cam.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
