using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Modelos feitos no Blender (pasta Blender/ do projecto, exportados por fsblend.py) em Resources/Models/FS/*.bytes.
    /// Formato FSM1: por peça, nome, materiais (um por submalha), posições, normais, UVs e índices. Os nomes dos
    /// materiais ("suit", "ivory", "tint"…) são resolvidos em jogo para a paleta e a cor do jogador.
    /// Não depende do importador de FBX do Unity: é lido em runtime e cacheado.
    /// </summary>
    public static class FsModels
    {
        public sealed class Model
        {
            public string Name;
            public Mesh Mesh;
            public string[] Materials;
        }

        static readonly Dictionary<string, Dictionary<string, Model>> s_files = new Dictionary<string, Dictionary<string, Model>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay() => s_files.Clear();

        /// <summary>Peça <paramref name="name"/> do ficheiro <paramref name="file"/>, ou null se não existir.</summary>
        public static Model Get(string file, string name)
        {
            var models = Load(file);
            return models != null && models.TryGetValue(name, out var m) ? m : null;
        }

        public static bool Has(string file) => Load(file) != null;

        static Dictionary<string, Model> Load(string file)
        {
            if (s_files.TryGetValue(file, out var cached)) return cached;
            Dictionary<string, Model> result = null;
            try
            {
                var asset = Resources.Load<TextAsset>("Models/FS/" + file);
                if (asset != null) result = Parse(asset.bytes, file);
            }
            catch (Exception e)
            {
                Debug.LogWarning("FriendSlop: modelo " + file + " inválido (" + e.Message + ") — a usar as formas procedurais.");
                result = null;
            }
            s_files[file] = result;
            return result;
        }

        static string ReadString(BinaryReader r)
        {
            int len = r.ReadInt32();
            return Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        static Dictionary<string, Model> Parse(byte[] data, string file)
        {
            var models = new Dictionary<string, Model>();
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "FSM1") throw new InvalidDataException("cabeçalho");
                int count = r.ReadInt32();
                for (int o = 0; o < count; o++)
                {
                    string name = ReadString(r);
                    int subCount = r.ReadInt32();
                    var mats = new string[subCount];
                    for (int i = 0; i < subCount; i++) mats[i] = ReadString(r);
                    int vc = r.ReadInt32();
                    var pos = new Vector3[vc];
                    var nrm = new Vector3[vc];
                    var uv = new Vector2[vc];
                    for (int i = 0; i < vc; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < vc; i++) nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < vc; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    var mesh = new Mesh { name = file + "_" + name };
                    if (vc > 65000) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = pos;
                    mesh.normals = nrm;
                    mesh.uv = uv;
                    mesh.subMeshCount = subCount;
                    for (int s = 0; s < subCount; s++)
                    {
                        int ic = r.ReadInt32();
                        var idx = new int[ic];
                        for (int i = 0; i < ic; i++) idx[i] = r.ReadInt32();
                        mesh.SetTriangles(idx, s, false);
                    }
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    mesh.UploadMeshData(false);
                    models[name] = new Model { Name = name, Mesh = mesh, Materials = mats };
                }
            }
            return models;
        }

        /// <summary>Cria a peça como filho de <paramref name="parent"/>, resolvendo cada material pelo nome.</summary>
        public static GameObject Spawn(Model model, Transform parent, Vector3 localPosition, Quaternion localRotation, Vector3 localScale,
            Func<string, Material> resolve, List<Renderer> renderers = null)
        {
            var go = new GameObject(model.Name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = model.Mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[model.Materials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = resolve(model.Materials[i]);
            mr.sharedMaterials = mats;
            renderers?.Add(mr);
            return go;
        }
    }
}
