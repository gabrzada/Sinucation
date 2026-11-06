using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FriendSlop.Presentation
{
    /// <summary>Opções gráficas do jogador (todas do mínimo ao máximo).</summary>
    [Serializable]
    public sealed class GraphicsOptions
    {
        public int Preset = 2;           // 0 Baixo, 1 Médio, 2 Alto, 3 Ultra, 4 Personalizado
        public int TextureQuality = 3;   // 0 ⅛ … 3 máxima
        public int Anisotropic = 3;      // 0 desligado, 1 2x, 2 4x, 3 8x, 4 16x
        public int Shadows = 3;          // 0 desligadas … 4 ultra
        public int Effects = 2;          // 0 baixo … 3 ultra (partículas, gosma, público, reflexos)
        public int PostProcessing = 3;   // 0 desligado … 3 alto
        public int AntiAliasing = 2;     // 0 nenhum, 1 FXAA, 2 SMAA, 3 MSAA 2x, 4 MSAA 4x, 5 TAA
        public bool Bloom = true;
        public bool AmbientOcclusion = true;
        public bool DepthOfField = true;
        public bool MotionBlur;
        public float RenderScale = 1f;
        public int Upscaler;             // 0 bilinear, 1 FSR 1.0, 2 STP, 3 DLSS (quando disponível)
        public bool VSync = true;
        public int FrameCap = 2;         // índice em FrameCaps
        public int DisplayMode;          // 0 ecrã inteiro (janela sem bordas), 1 exclusivo, 2 janela
        public int ResolutionIndex = -1; // -1 = nativa

        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 240, 0 };

        public void ApplyPreset(int preset)
        {
            Preset = preset;
            switch (preset)
            {
                case 0:
                    TextureQuality = 1; Anisotropic = 0; Shadows = 1; Effects = 0; PostProcessing = 1; AntiAliasing = 1;
                    Bloom = true; AmbientOcclusion = false; DepthOfField = false; RenderScale = 0.77f; Upscaler = 1;
                    break;
                case 1:
                    TextureQuality = 2; Anisotropic = 2; Shadows = 2; Effects = 1; PostProcessing = 2; AntiAliasing = 2;
                    Bloom = true; AmbientOcclusion = false; DepthOfField = false; RenderScale = 1f; Upscaler = 0;
                    break;
                case 2:
                    TextureQuality = 3; Anisotropic = 3; Shadows = 3; Effects = 2; PostProcessing = 3; AntiAliasing = 2;
                    Bloom = true; AmbientOcclusion = true; DepthOfField = true; RenderScale = 1f; Upscaler = 0;
                    break;
                case 3:
                    TextureQuality = 3; Anisotropic = 4; Shadows = 4; Effects = 3; PostProcessing = 3; AntiAliasing = 5;
                    Bloom = true; AmbientOcclusion = true; DepthOfField = true; RenderScale = 1f; Upscaler = 0;
                    break;
            }
        }

        public string ToJson() => JsonUtility.ToJson(this);
        public static GraphicsOptions FromJson(string json)
        {
            var o = new GraphicsOptions();
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, o); }
                catch (Exception) { o = new GraphicsOptions(); }
            }
            return o;
        }
    }

    /// <summary>
    /// Aplica as opções gráficas: cópia em tempo de execução do asset URP (não suja o projecto), volume de
    /// pós-processamento (ACES, bloom, cor, vinheta, grão, DOF, motion blur), SSAO do renderer, sombras,
    /// AA, escala/upscaler, VSync, limite de FPS e resolução.
    /// </summary>
    public static class GraphicsQuality
    {
        static UniversalRenderPipelineAsset s_asset;
        static RenderPipelineAsset s_original;
        static VolumeProfile s_profile;
        static Volume s_volume;
        static Bloom s_bloom;
        static Vignette s_vignette;
        static DepthOfField s_dof;
        static MotionBlur s_motion;
        static FilmGrain s_grain;
        static ChromaticAberration s_chroma;
        static ColorAdjustments s_color;
        static readonly List<(ScriptableRendererFeature feature, bool original)> s_features = new List<(ScriptableRendererFeature, bool)>();
        static float s_baseVignette = 0.28f;
        static int s_aniso = 8;

        public static GraphicsOptions Options { get; private set; } = new GraphicsOptions();
        public static float EffectsScale => Options.Effects == 0 ? 0.35f : Options.Effects == 1 ? 0.6f : Options.Effects == 2 ? 1f : 1.35f;
        public static int CrowdCount => Options.Effects == 0 ? 0 : Options.Effects == 1 ? 6 : Options.Effects == 2 ? 11 : 15;
        public static IReadOnlyList<Light> ShadowLights;
        public static ParticleSystem Dust;
        public static ReflectionProbe Probe;

        /// <summary>DLSS existe no SRP do Unity 6.7 mas está atrás do "upscaler framework", desligado nesta versão.</summary>
        public static bool DlssAvailable
        {
            get
            {
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ENABLE_NVIDIA_MODULE
                return SystemInfo.graphicsDeviceVendor != null && SystemInfo.graphicsDeviceVendor.ToUpperInvariant().Contains("NVIDIA");
#else
                return false;
#endif
            }
        }

        public static string DlssStatus =>
#if ENABLE_UPSCALER_FRAMEWORK && ENABLE_NVIDIA && ENABLE_NVIDIA_MODULE
            DlssAvailable ? "DLSS disponível" : "DLSS precisa de uma placa NVIDIA RTX";
#else
            "DLSS: o motor (Unity 6.7 beta) tem o código, mas o 'upscaler framework' ainda está desligado nesta versão";
#endif

        public static void Initialize(Camera camera, GraphicsOptions options)
        {
            Options = options;
            var current = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (current is UniversalRenderPipelineAsset urp && s_asset == null)
            {
                s_original = QualitySettings.renderPipeline;
                s_asset = UnityEngine.Object.Instantiate(urp);
                s_asset.name = urp.name + " (runtime)";
                QualitySettings.renderPipeline = s_asset;
                CollectFeatures(s_asset);
                Application.quitting += Restore;
            }

            BuildVolume();
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.requiresDepthTexture = true;
            Apply(camera);
        }

        static void CollectFeatures(UniversalRenderPipelineAsset asset)
        {
            s_features.Clear();
            var field = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            if (!(field?.GetValue(asset) is ScriptableRendererData[] list)) return;
            foreach (var data in list)
            {
                if (data == null) continue;
                foreach (var f in data.rendererFeatures)
                    if (f != null) s_features.Add((f, f.isActive));
            }
        }

        /// <summary>Repõe os renderer features ao sair (no editor são assets partilhados).</summary>
        static void Restore()
        {
            foreach (var (feature, original) in s_features)
                if (feature != null) feature.SetActive(original);
            if (s_asset != null) QualitySettings.renderPipeline = s_original;
        }

        static void BuildVolume()
        {
            if (s_volume != null) return;
            var go = new GameObject("PostProcessVolume");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_volume = go.AddComponent<Volume>();
            s_volume.isGlobal = true;
            s_volume.priority = 10f;
            s_profile = ScriptableObject.CreateInstance<VolumeProfile>();
            s_volume.sharedProfile = s_profile;

            var tone = s_profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            s_bloom = s_profile.Add<Bloom>(true);
            s_bloom.threshold.Override(1.05f);
            s_bloom.intensity.Override(0.9f);
            s_bloom.scatter.Override(0.72f);
            s_bloom.tint.Override(new Color(1f, 0.9f, 0.8f));
            s_color = s_profile.Add<ColorAdjustments>(true);
            s_color.postExposure.Override(0.1f);
            s_color.contrast.Override(14f);
            s_color.saturation.Override(12f);
            var wb = s_profile.Add<WhiteBalance>(true);
            wb.temperature.Override(8f);
            wb.tint.Override(4f);
            var smh = s_profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.92f, 0.94f, 1.08f, 0f));
            smh.highlights.Override(new Vector4(1.06f, 1.02f, 0.94f, 0f));
            s_vignette = s_profile.Add<Vignette>(true);
            s_vignette.intensity.Override(s_baseVignette);
            s_vignette.smoothness.Override(0.45f);
            s_grain = s_profile.Add<FilmGrain>(true);
            s_grain.type.Override(FilmGrainLookup.Thin1);
            s_grain.intensity.Override(0.12f);
            s_chroma = s_profile.Add<ChromaticAberration>(true);
            s_chroma.intensity.Override(0.06f);
            s_dof = s_profile.Add<DepthOfField>(true);
            s_dof.mode.Override(DepthOfFieldMode.Gaussian);
            s_dof.gaussianStart.Override(4f);
            s_dof.gaussianEnd.Override(14f);
            s_dof.gaussianMaxRadius.Override(1f);
            s_dof.highQualitySampling.Override(true);
            s_motion = s_profile.Add<MotionBlur>(true);
            s_motion.intensity.Override(0.25f);
        }

        public static void ConfigureTexture(Texture texture)
        {
            if (texture == null) return;
            texture.anisoLevel = Options.Anisotropic == 0 ? 1 : s_aniso;
            texture.filterMode = FilterMode.Trilinear;
        }

        /// <summary>Profundidade de campo: desfoca o fundo quando a câmara de tacada está perto da mesa.</summary>
        public static void SetFocus(float nearDistance, bool shotMode)
        {
            if (s_dof == null) return;
            s_dof.gaussianStart.Override(shotMode ? nearDistance + 0.8f : 4f);
            s_dof.gaussianEnd.Override(shotMode ? nearDistance + 5f : 14f);
        }

        /// <summary>Vinheta extra do debuff "Visão de Túnel" (só no cliente afectado).</summary>
        public static void SetExtraVignette(float amount)
        {
            if (s_vignette == null) return;
            float base_ = Options.PostProcessing > 0 ? s_baseVignette : 0f;
            s_vignette.active = base_ > 0f || amount > 0f;
            s_vignette.intensity.Override(Mathf.Clamp01(Mathf.Max(base_, amount)));
            s_vignette.smoothness.Override(amount > 0f ? 1f : 0.45f);
        }

        public static void Apply(Camera camera)
        {
            var o = Options;

            // Texturas e filtro anisotrópico.
            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(3 - o.TextureQuality, 0, 3);
            s_aniso = o.Anisotropic == 0 ? 1 : 1 << o.Anisotropic;
            QualitySettings.anisotropicFiltering = o.Anisotropic == 0 ? AnisotropicFiltering.Disable : AnisotropicFiltering.ForceEnable;
            if (o.Anisotropic > 0) Texture.SetGlobalAnisotropicFilteringLimits(s_aniso, s_aniso);
            TextureFactory.ReapplyFiltering();

            // Sombras.
            if (s_asset != null)
            {
                int[] distance = { 0, 12, 20, 30, 45 };
                int[] resolution = { 256, 512, 1024, 2048, 4096 };
                s_asset.shadowDistance = distance[Mathf.Clamp(o.Shadows, 0, 4)];
                s_asset.shadowCascadeCount = o.Shadows >= 4 ? 4 : o.Shadows >= 3 ? 2 : 1;
                s_asset.mainLightShadowmapResolution = resolution[Mathf.Clamp(o.Shadows, 0, 4)];
                s_asset.additionalLightsShadowmapResolution = resolution[Mathf.Clamp(o.Shadows, 0, 4)];
                SetPrivate(s_asset, "m_AdditionalLightShadowsSupported", o.Shadows > 0);
                SetPrivate(s_asset, "m_SoftShadowsSupported", o.Shadows >= 2);
                s_asset.renderScale = Mathf.Clamp(o.RenderScale, 0.5f, 1f);
                s_asset.msaaSampleCount = o.AntiAliasing == 3 ? 2 : o.AntiAliasing == 4 ? 4 : 1;
#pragma warning disable CS0618
                s_asset.upscalingFilter = o.Upscaler == 1 ? UpscalingFilterSelection.FSR : o.Upscaler == 2 ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Linear;
#pragma warning restore CS0618
            }
            if (ShadowLights != null)
                foreach (var light in ShadowLights)
                {
                    if (light == null) continue;
                    light.shadows = o.Shadows == 0 ? LightShadows.None : o.Shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
                    light.shadowResolution = o.Shadows >= 4 ? UnityEngine.Rendering.LightShadowResolution.VeryHigh : o.Shadows >= 3 ? UnityEngine.Rendering.LightShadowResolution.High : UnityEngine.Rendering.LightShadowResolution.Medium;
                }

            // Pós-processamento.
            bool post = o.PostProcessing > 0;
            if (camera != null)
            {
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = post || o.AntiAliasing == 1 || o.AntiAliasing == 2;
                data.antialiasing = o.AntiAliasing == 1 ? AntialiasingMode.FastApproximateAntialiasing :
                                    o.AntiAliasing == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing :
                                    o.AntiAliasing == 5 ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
                camera.allowMSAA = o.AntiAliasing == 3 || o.AntiAliasing == 4;
            }
            if (s_profile != null)
            {
                foreach (var c in s_profile.components)
                    c.active = post;
                s_bloom.active = post && o.Bloom;
                s_bloom.highQualityFiltering.Override(o.PostProcessing >= 3);
                s_grain.active = post && o.PostProcessing >= 3;
                s_chroma.active = post && o.PostProcessing >= 3;
                s_dof.active = post && o.DepthOfField && o.PostProcessing >= 2;
                s_motion.active = post && o.MotionBlur;
                s_color.active = post;
            }
            foreach (var (feature, _) in s_features)
                if (feature != null && feature.GetType().Name == "ScreenSpaceAmbientOcclusion")
                    feature.SetActive(o.AmbientOcclusion && o.PostProcessing >= 1);

            // Efeitos e reflexos.
            if (Dust != null)
            {
                var em = Dust.emission;
                em.rateOverTime = 30f * EffectsScale;
            }
            if (Probe != null)
            {
                Probe.resolution = o.Effects >= 3 ? 512 : o.Effects >= 1 ? 256 : 64;
                Probe.RenderProbe();
            }

            // Ecrã.
            QualitySettings.vSyncCount = o.VSync ? 1 : 0;
            int cap = GraphicsOptions.FrameCaps[Mathf.Clamp(o.FrameCap, 0, GraphicsOptions.FrameCaps.Length - 1)];
            Application.targetFrameRate = o.VSync ? -1 : (cap <= 0 ? -1 : cap);
        }

        /// <summary>Algumas opções do asset URP só têm getter público; o campo serializado é alterado por reflexão.</summary>
        static void SetPrivate(object target, string field, bool value)
        {
            var f = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool)) f.SetValue(target, value);
        }

        public static void ApplyDisplay()
        {
            var o = Options;
            var mode = o.DisplayMode == 1 ? FullScreenMode.ExclusiveFullScreen : o.DisplayMode == 2 ? FullScreenMode.Windowed : FullScreenMode.FullScreenWindow;
            var resolutions = Screen.resolutions;
            if (o.ResolutionIndex >= 0 && o.ResolutionIndex < resolutions.Length)
                Screen.SetResolution(resolutions[o.ResolutionIndex].width, resolutions[o.ResolutionIndex].height, mode);
            else
                Screen.fullScreenMode = mode;
        }
    }
}
