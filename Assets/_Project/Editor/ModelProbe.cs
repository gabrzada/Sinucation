using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FriendSlop.EditorTools
{
    /// <summary>
    /// Diagnóstico (batch): tamanho, pivô e hierarquia dos modelos em Resources/Models e clips das personagens.
    /// Unity -batchmode -executeMethod FriendSlop.EditorTools.ModelProbe.Run -probeOut relatorio.txt
    /// </summary>
    public static class ModelProbe
    {
        public static void Run()
        {
            string output = "model_probe.txt";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-probeOut") output = args[i + 1];

            AssetDatabase.Refresh();
            var sb = new StringBuilder();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Resources/Models" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                var go = Object.Instantiate(prefab);
                var bounds = new Bounds(go.transform.position, Vector3.zero);
                bool any = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!any) bounds = r.bounds;
                    else bounds.Encapsulate(r.bounds);
                    any = true;
                }
                sb.AppendLine(path.Replace("Assets/_Project/Resources/Models/", "") + " size=" + V(bounds.size) + " center=" + V(bounds.center) + " min=" + V(bounds.min) +
                              " renderers=" + go.GetComponentsInChildren<Renderer>().Length + " mats=" + MaterialNames(go));
                if (path.Contains("/Characters/"))
                {
                    foreach (var t in go.GetComponentsInChildren<Transform>())
                        sb.AppendLine("   bone " + t.name + " pos=" + V(t.position) + " rot=" + V(t.eulerAngles) + " scale=" + V(t.lossyScale));
                    foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                        if (asset is AnimationClip clip && !clip.name.StartsWith("__preview"))
                            sb.AppendLine("   clip " + clip.name + " len=" + clip.length.ToString("0.00") + " loop=" + clip.isLooping + " legacy=" + clip.legacy);
                    // Perfil da malha do corpo por faixas de altura (para encaixar arnês, cinto e solas).
                    foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                    {
                        var mesh = smr.sharedMesh;
                        if (mesh == null) continue;
                        var verts = mesh.vertices;
                        var weights = mesh.boneWeights;
                        sb.AppendLine("   mesh " + smr.name + " verts=" + verts.Length + " bounds=" + V(mesh.bounds.size) + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name : "-") +
                                      " bones=" + string.Join(",", System.Array.ConvertAll(smr.bones, b => b.name)));
                        for (float y0 = 0f; y0 < 0.8f; y0 += 0.02f)
                        {
                            float minX = 9, maxX = -9, minZ = 9, maxZ = -9, cMinX = 9, cMaxX = -9, cMaxZ = -9;
                            int n = 0;
                            for (int v = 0; v < verts.Length; v++)
                            {
                                var p = verts[v];
                                if (p.y < y0 || p.y >= y0 + 0.02f) continue;
                                n++;
                                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
                                minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                                int bone = weights.Length > v ? weights[v].boneIndex0 : -1;
                                string bn = bone >= 0 && bone < smr.bones.Length ? smr.bones[bone].name : "";
                                if (bn == "torso" || bn == "root" || bn.StartsWith("leg"))
                                {
                                    cMinX = Mathf.Min(cMinX, p.x); cMaxX = Mathf.Max(cMaxX, p.x); cMaxZ = Mathf.Max(cMaxZ, p.z);
                                }
                            }
                            if (n > 0)
                                sb.AppendLine("     y" + y0.ToString("0.00") + " n=" + n + " x[" + minX.ToString("0.000") + "," + maxX.ToString("0.000") + "] z[" + minZ.ToString("0.000") + "," + maxZ.ToString("0.000") +
                                              "] corpo x[" + cMinX.ToString("0.000") + "," + cMaxX.ToString("0.000") + "] zFrente " + cMaxZ.ToString("0.000"));
                        }
                    }
                    var animator = go.GetComponent<Animator>();
                    sb.AppendLine("   animator=" + (animator != null) + " avatar=" + (animator != null && animator.avatar != null));
                }
                Object.DestroyImmediate(go);
            }
            File.WriteAllText(output, sb.ToString());
            Debug.Log("[ModelProbe] escrito " + output);
        }

        static string V(Vector3 v) => v.x.ToString("0.###") + "," + v.y.ToString("0.###") + "," + v.z.ToString("0.###");

        static string MaterialNames(GameObject go)
        {
            var sb = new StringBuilder();
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null) sb.Append(m.name).Append('(').Append(m.shader != null ? m.shader.name : "?").Append(")|");
            return sb.ToString();
        }
    }
}
