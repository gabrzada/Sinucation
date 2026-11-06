using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FriendSlop.Presentation
{
    // Saved indices 0–3 retain their previous meaning.
    public static class UpscalerController
    {
        public static readonly string[] Names = { "Nenhum (escala simples)", "AMD FSR 1.0", "Unity STP (temporal)", "NVIDIA DLSS 4.5", "AMD FSR Upscaling 4.1.1", "Intel XeSS" };
        public static readonly string[] Ids = { "unity.bilinear", "amd.fsr1", "unity.stp", "nvidia.dlss4", "amd.fsr4", "intel.xess" };
        static GraphicsOptions s_options;
        static int s_applied = -1;
        static float s_scale = -1f;
        static int s_quality = -1;
        static RenderPipeline s_pipeline;
        public static string Status { get; private set; } = "A inicializar upscalers…";
        public static bool IsTemporal(int index) => index >= 2 && index < Ids.Length;
        /// <summary>DLSS, FSR 4 e XeSS: o modo de qualidade do próprio SDK dita a resolução interna (não a escala de render).</summary>
        public static bool UsesQualityMode(int index) => index >= 3 && index < Ids.Length;
        public const int NvidiaVendor = 0x10de, AmdVendor = 0x1002, IntelVendor = 0x8086;
        public static bool MatchesVendor(int index, int vendor) => index == 3 ? vendor == NvidiaVendor :
            index == 4 || index == 1 ? vendor == AmdVendor : index == 5 ? vendor == IntelVendor : index == 0 || index == 2;

        /// <summary>Só para QA: -fsAllUpscalers mostra todos os upscalers que o SDK suporte, ignorando o fabricante.</summary>
        static readonly bool s_ignoreVendor = Array.IndexOf(Environment.GetCommandLineArgs(), "-fsAllUpscalers") >= 0;

        /// <summary>Upscaler efectivamente usado (o pedido, ou o recurso seguro se não houver suporte).</summary>
        public static int Resolve(int requested)
        {
            requested = Mathf.Clamp(requested, 0, Ids.Length - 1);
            return Available(requested) ? requested : Available(2) ? 2 : 0;
        }

        /// <summary>O upscaler do fabricante desta placa (DLSS/FSR 4/XeSS) se estiver disponível, senão −1.</summary>
        public static int VendorUpscaler()
        {
            for (int i = 3; i < Ids.Length; i++)
                if (Available(i)) return i;
            return -1;
        }

        /// <summary>Nome do modo nativo de cada SDK (o DLSS chama-lhe DLAA).</summary>
        public static string QualityName(int upscaler, int quality)
        {
            if (quality <= 0) return upscaler == 3 ? "DLAA (nativo)" : "Nativo (AA)";
            return QualityNames[Mathf.Clamp(quality, 0, QualityNames.Length - 1)];
        }

        public static int[] MenuChoices()
        {
            var choices = new System.Collections.Generic.List<int> { 0, 2 };
            for (int i = 3; i < Ids.Length; i++) if (Available(i)) choices.Add(i);
            return choices.ToArray();
        }

        public static readonly string[] QualityNames = { "Nativo", "Qualidade", "Equilibrado", "Desempenho", "Ultra desempenho" };
        public static readonly float[] QualityScales = { 1f, 2f / 3f, 1f / 1.7f, .5f, 1f / 3f };
        public static int QualityForScale(float scale)
        {
            int best = 0;
            for (int i = 1; i < QualityScales.Length; i++)
                if (Mathf.Abs(scale - QualityScales[i]) < Mathf.Abs(scale - QualityScales[best])) best = i;
            return best;
        }

#if ENABLE_UPSCALER_FRAMEWORK
        // Unity 6.7 exposes selection publicly, but the device capabilities and scaling mode remain internal.
        static readonly FieldInfo SystemField = typeof(UniversalRenderPipeline).GetField("upscaling", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly FieldInfo ModeField = typeof(UniversalRenderPipelineAsset).GetField("m_ScalingMode", BindingFlags.Instance | BindingFlags.NonPublic);
        static Upscaling System => SystemField?.GetValue(null) as Upscaling;
#endif
        public static bool Available(int index)
        {
            if (!s_ignoreVendor && !MatchesVendor(index, SystemInfo.graphicsDeviceVendorID)) return false;
#if ENABLE_UPSCALER_FRAMEWORK
            return index >= 0 && index < Ids.Length && System?.GetIUpscalerById(Ids[index])?.isSupportedOnDevice == true;
#else
            return index >= 0 && index <= 2;
#endif
        }

        public static void Initialize(UniversalRenderPipelineAsset asset, GraphicsOptions options)
        {
            s_options = options;
            s_applied = -1;
            s_pipeline = null;
#if ENABLE_UPSCALER_FRAMEWORK
            ModeField?.SetValue(asset, Enum.ToObject(ModeField.FieldType, 1));
            // The asset clone otherwise shares these ScriptableObjects with the editor project.
            for (int i = 0; i < asset.upscalerOptions.Count; i++)
                if (asset.upscalerOptions[i] != null) asset.upscalerOptions[i] = UnityEngine.Object.Instantiate(asset.upscalerOptions[i]);
#endif
            RenderPipelineManager.beginContextRendering -= BeforeRendering;
            RenderPipelineManager.beginContextRendering += BeforeRendering;
        }

        static void BeforeRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras) => Apply();

        public static void Apply()
        {
            if (s_options == null) return;
#if ENABLE_UPSCALER_FRAMEWORK
            var pipeline = RenderPipelineManager.currentPipeline as UniversalRenderPipeline;
            var system = System;
            if (pipeline == null || system == null) return;
            int requested = Mathf.Clamp(s_options.Upscaler, 0, Ids.Length - 1);
            int quality = Mathf.Clamp(s_options.UpscalerQuality, 0, QualityNames.Length - 1);
            if (s_applied == requested && s_pipeline == pipeline && Mathf.Approximately(s_scale, s_options.RenderScale) && s_quality == quality) return;
            int actual = Resolve(requested);
            var upscaler = system.GetIUpscalerById(Ids[actual]);
            var options = system.GetGlobalOptions(upscaler);
            // DLSS/FSR 4/XeSS: o preset do SDK dita a resolução interna. Em "CustomScaling" o URP limitava a escala ao
            // intervalo do modo de qualidade por omissão e baixar a escala de render quase não mudava a imagem.
            // STP, bilinear e FSR 1 continuam a seguir a escala de render.
            if (options != null)
                options.resolutionMode = UsesQualityMode(actual) ? UpscalerResolutionMode.QualityMode : UpscalerResolutionMode.CustomScaling;
            if (options is XeSSOptions xess) xess.quality = XeSSOptions.QualityForMode(quality);
#if ENABLE_NVIDIA && ENABLE_NVIDIA_MODULE
            if (options is DLSSOptions dlss)
            {
                dlss.dlssQualityMode = quality == 0 ? UnityEngine.NVIDIA.DLSSQuality.DLAA :
                    quality == 1 ? UnityEngine.NVIDIA.DLSSQuality.MaximumQuality :
                    quality == 2 ? UnityEngine.NVIDIA.DLSSQuality.Balanced :
                    quality == 3 ? UnityEngine.NVIDIA.DLSSQuality.MaximumPerformance : UnityEngine.NVIDIA.DLSSQuality.UltraPerformance;
                dlss.dlssRenderPresetQuality = UnityEngine.NVIDIA.DLSSPreset.Preset_M;
                dlss.dlssRenderPresetBalanced = UnityEngine.NVIDIA.DLSSPreset.Preset_M;
                dlss.dlssRenderPresetPerformance = UnityEngine.NVIDIA.DLSSPreset.Preset_M;
                dlss.dlssRenderPresetUltraPerformance = UnityEngine.NVIDIA.DLSSPreset.Preset_L;
                dlss.dlssRenderPresetDLAA = UnityEngine.NVIDIA.DLSSPreset.Preset_M;
            }
#endif
#if ENABLE_AMD && ENABLE_AMD_MODULE
            if (options is FSR4Options fsr)
            {
                fsr.fsr4QualityMode = quality == 0 ? UnityEngine.AMD.FSR4Quality.NativeAA :
                    quality == 1 ? UnityEngine.AMD.FSR4Quality.Quality : quality == 2 ? UnityEngine.AMD.FSR4Quality.Balanced :
                    quality == 3 ? UnityEngine.AMD.FSR4Quality.Performance : UnityEngine.AMD.FSR4Quality.UltraPerformance;
                fsr.enableSharpening = true;
                fsr.sharpness = .2f;
            }
#endif
            if (!pipeline.SetUpscaler(Ids[actual])) return;
            s_pipeline = pipeline;
            s_applied = requested;
            s_scale = s_options.RenderScale;
            s_quality = quality;
            string mode = UsesQualityMode(actual) ? " (" + QualityName(actual, quality) + ")" : "";
            Status = actual == requested ? Names[actual] + mode + " ativo." : Names[requested] + " indisponível neste sistema; usando " + Names[actual] + ".";
            Debug.Log("[Upscaler] " + Status + " Render scale=" + s_options.RenderScale + " modo=" + quality);
#else
            Status = "Bilinear, FSR 1.0 e STP disponíveis nesta plataforma.";
#endif
        }
    }
}
