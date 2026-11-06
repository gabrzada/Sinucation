using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FriendSlop.Presentation
{
    /// <summary>Reports the actual scene color target before reconstruction, without adding GPU work.</summary>
    public static class RenderResolutionMonitor
    {
        static readonly Probe Pass = new Probe();
        public static Vector2Int Internal { get; private set; }
        public static Vector2Int Output { get; private set; }
        public static string Status => $"Interna: {Internal.x}×{Internal.y} · saída: {Output.x}×{Output.y}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            RenderPipelineManager.beginCameraRendering -= Begin;
            RenderPipelineManager.beginCameraRendering += Begin;
        }

        static void Begin(ScriptableRenderContext context, Camera camera)
        {
            if (camera.cameraType != CameraType.Game || camera.targetTexture != null) return;
            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) && data.renderType == CameraRenderType.Base)
                data.scriptableRenderer.EnqueuePass(Pass);
        }

        sealed class Probe : ScriptableRenderPass
        {
            public Probe() => renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                var camera = frame.Get<UniversalCameraData>();
                var source = frame.Get<UniversalResourceData>().activeColorTexture;
                if (!source.IsValid()) return;
                var desc = source.GetDescriptor(graph);
                var input = new Vector2Int(desc.width, desc.height);
                var output = new Vector2Int(camera.camera.pixelWidth, camera.camera.pixelHeight);
                if (Internal == input && Output == output) return;
                Internal = input;
                Output = output;
                Debug.Log("[RenderResolution] " + Status);
            }
        }
    }
}
