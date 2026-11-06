using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FriendSlop.Presentation
{
    /// <summary>
    /// Contorno a tinta dos estilos cel (à Borderlands): deteção de arestas em ecrã a partir da profundidade e das
    /// normais da câmara, antes do pós-processamento. "Cel suave" traça linhas finas; "Cel marcado" linhas grossas
    /// e pretas. Não precisa de mexer no Renderer Data: o passe é enfileirado em cada câmara de jogo.
    /// </summary>
    public static class InkOutline
    {
        static readonly int ParamsId = Shader.PropertyToID("_FSInk");
        static Material s_material;
        static Pass s_pass;
        static int s_style;
        static bool s_hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticsOnPlay()
        {
            if (s_hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            s_hooked = false;
            s_style = 0;
            s_material = null;
            s_pass = null;
        }

        /// <summary>0 original (sem contorno), 1 cel suave, 2 cel marcado.</summary>
        public static void SetStyle(int style)
        {
            s_style = Mathf.Clamp(style, 0, 2);
            if (s_style == 0 || s_hooked) return;
            var shader = Resources.Load<Shader>("Shaders/InkOutline");
            if (shader == null || !shader.isSupported) return;
            s_material = CoreUtils.CreateEngineMaterial(shader);
            s_pass = new Pass();
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            s_hooked = true;
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (s_style == 0 || s_material == null || camera.cameraType != CameraType.Game) return;
            if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) || data.renderType != CameraRenderType.Base) return;
            bool marked = s_style == 2;
            // x: espessura em píxeis a 1080p, y: opacidade da tinta, z: limiar de profundidade relativa, w: limiar das normais.
            s_material.SetVector(ParamsId, marked ? new Vector4(3f, 0.95f, 0.045f, 0.55f) : new Vector4(1.6f, 0.6f, 0.06f, 0.7f));
            s_pass.Material = s_material;
            data.scriptableRenderer.EnqueuePass(s_pass);
        }

        sealed class Pass : ScriptableRenderPass
        {
            public Material Material;

            sealed class PassData
            {
                public TextureHandle Source;
                public Material Material;
            }

            public Pass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                profilingSampler = new ProfilingSampler("FriendSlop Ink Outline");
                ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                if (Material == null || resources.isActiveTargetBackBuffer) return;
                var source = resources.activeColorTexture;
                if (!source.IsValid()) return;

                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_FSInkOutlineColor";
                desc.clearBuffer = false;
                var destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("FriendSlop Ink Outline", out var data, profilingSampler))
                {
                    data.Source = source;
                    data.Material = Material;
                    builder.UseTexture(source, AccessFlags.Read);
                    if (resources.cameraDepthTexture.IsValid()) builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    if (resources.cameraNormalsTexture.IsValid()) builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                        Blitter.BlitTexture(ctx.cmd, d.Source, new Vector4(1f, 1f, 0f, 0f), d.Material, 0));
                }
                resources.cameraColor = destination;
            }
        }
    }
}
