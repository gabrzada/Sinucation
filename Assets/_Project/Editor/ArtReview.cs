using System;
using System.IO;
using System.Linq;
using FriendSlop.Billiards;
using FriendSlop.Core;
using FriendSlop.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    public static class ArtReview
    {
        public static void Capture()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use a separate batch editor.");
            var args = Environment.GetCommandLineArgs();
            bool before = args.Contains("-artBefore");
            string dir = "Blender/art-review/" + (before ? "before" : "after");
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            UnityEngine.Random.InitState(91);
            var physics = new BilliardsPhysicsSettings();
            var room = new GameObject("ReviewRoom").AddComponent<RoomBuilder>();
            room.Build(physics.TableLength * .5f, physics.TableWidth * .5f);
            new GameObject("ReviewTable").AddComponent<TableView>().Initialize(physics);
            for (int i = 0; i < 3; i++)
            {
                var actor = new GameObject("Actor");
                actor.transform.position = new Vector3(-1.1f + i * 1.1f, 0, 1.5f);
                actor.transform.rotation = Quaternion.Euler(0, 180, 0);
                AvatarVisual.Create(actor.transform, new PlayerProfile("Review", (Archetype)i, ArtPalette.Heads[i]));
            }
            for (int i = 0; i < 10; i++)
                LowPoly.Part(ToyMeshes.Sphere, "Ball", null, new Vector3(-.75f + (i % 5) * .31f, TableView.TopY + physics.BallRadius, -.24f + i / 5 * .37f),
                    Vector3.one * physics.BallRadius * 2, VisualFactory.ClearCoat(VisualFactory.BallTexture(i)));
            // Same positions and camera for the old and refined asset gallery.
            var props = new GameObject("Gallery").transform;
            PropLibrary.Spawn("Survival/barrel", props, new Vector3(-1.0f, 0, 20), 20, .95f);
            PropLibrary.Spawn("Factory/box-large", props, new Vector3(0, 0, 20), 15, .7f);
            PropLibrary.Spawn("Furniture/stoolBar", props, new Vector3(1.0f, 0, 20), 0, .78f);
            var stage = VisualFactory.Primitive(PrimitiveType.Cube, "GalleryFloor", props,
                new Vector3(0,-.035f,20),new Vector3(200,.06f,200),VisualFactory.Lit(new Color(.63f,.66f,.7f),.2f));
            stage.SetActive(false);
            var key = new GameObject("GalleryKey").AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.8f;
            key.transform.rotation = Quaternion.Euler(35,-25,0);
            key.enabled = false;
            var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.20f,.24f,.30f);
            camera.fieldOfView = 48;
            camera.nearClipPlane = .04f;
            camera.farClipPlane = 60;
            var rt = new RenderTexture(1600, 1000, 24);
            camera.targetTexture = rt;
            for (int mode = 0; mode < (before ? 1 : 3); mode++)
            {
                VisualFactory.ApplyStyle(mode);
                Save(camera, rt, dir, "arena-" + mode, new Vector3(3.3f, 1.8f, -3.8f), new Vector3(0,.9f,.7f));
                Save(camera, rt, dir, "table-" + mode, new Vector3(1.6f, 1.15f, -1.8f), new Vector3(0,.48f,.15f));
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false;
                room.gameObject.SetActive(false);
                stage.SetActive(true);
                key.enabled = true;
                camera.farClipPlane = 10f;
                Save(camera, rt, dir, "assets-" + mode, new Vector3(1.6f, 1.45f, 22.95f), new Vector3(0,.48f,20));
                camera.farClipPlane = 60f;
                key.enabled = false;
                stage.SetActive(false);
                room.gameObject.SetActive(true);
                RenderSettings.fog = fog;
            }
            VisualFactory.ApplyStyle(0);
            camera.targetTexture = null;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            var shader = Resources.Load<Shader>("Shaders/CelLit");
            if (shader != null)
            {
                var messages = ShaderUtil.GetShaderMessages(shader);
                File.WriteAllLines(Path.Combine(dir, "shader-messages.txt"), messages.Select(m => m.severity + ": " + m.message + " at " + m.file + ":" + m.line));
                if (messages.Any(m => m.severity.ToString() == "Error")) throw new Exception("Cel shader errors; see shader-messages.txt");
            }
            Debug.Log("[ArtReview] Captured " + dir);
        }

        static void Save(Camera camera, RenderTexture rt, string dir, string name, Vector3 position, Vector3 target)
        {
            camera.transform.position = position;
            camera.transform.LookAt(target);
            // First render uploads procedural textures and material buffers in the batch editor.
            camera.Render();
            camera.Render();
            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(dir,name+".png"), image.EncodeToPNG());
            RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
