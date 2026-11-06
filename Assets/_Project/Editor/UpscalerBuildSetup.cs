using FriendSlop.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FriendSlop.EditorTools
{
    // All runtime choices must be present at build time, or URP strips their shaders/resources.
    public sealed class UpscalerBuildSetup : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;
        public void OnPreprocessBuild(BuildReport report) => Configure();

        [MenuItem("FriendSlop/Configurar upscalers")]
        public static void Configure()
        {
            foreach (var path in System.IO.Directory.GetFiles("Packages/com.intel.xess/RunTime/Bin", "*.dll"))
            {
                var plugin = AssetImporter.GetAtPath(path.Replace('\\', '/')) as PluginImporter;
                if (plugin == null) continue;
                plugin.SetCompatibleWithAnyPlatform(false);
                plugin.SetCompatibleWithEditor(true);
                plugin.SetEditorData("OS", "Windows");
                plugin.SetEditorData("CPU", "x86_64");
                plugin.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
                plugin.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
                plugin.isPreloaded = true;
                plugin.SaveAndReimport();
            }
#if ENABLE_UPSCALER_FRAMEWORK
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if (asset == null) throw new BuildFailedException("PC_RPAsset missing");
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_ScalingMode").enumValueIndex = 1;
            var priority = serialized.FindProperty("m_UpscalerPriority");
            priority.arraySize = UpscalerController.Ids.Length;
            for (int i = 0; i < priority.arraySize; i++)
            {
                var entry = priority.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("upscalerId").stringValue = UpscalerController.Ids[i];
                entry.FindPropertyRelative("upscalerName").stringValue = UpscalerController.Names[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[Upscaler] Build priority/resources configured; runtime selection remains in player settings.");
#endif
        }
    }
}
