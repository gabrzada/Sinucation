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
        public int CelShading;           // 0 original (default), 1 cel suave, 2 cel marcado; independent of quality preset
        public int PostProcessing = 3;   // 0 desligado … 3 alto
        public int AntiAliasing = 2;     // 0 nenhum, 1 FXAA, 2 SMAA, 3 MSAA 2x, 4 MSAA 4x, 5 TAA
        public bool Bloom = true;
        public bool AmbientOcclusion = true;
        /// <summary>Iluminação global em tempo real (Surface Cache GI do URP, ray tracing): luz a ressaltar nas paredes,
        /// no pano e nas máquinas. Pesada — só a predefinição Ultra a liga.</summary>
        public bool GlobalIllumination;
        public bool DepthOfField;         // desligada por omissão (os presets também não a ligam)
        public bool MotionBlur;
        public float RenderScale = 1f;
        public int Upscaler;             // Stable IDs: 0 bilinear, 1 FSR 1, 2 STP, 3 DLSS 4.5, 4 FSR 4, 5 XeSS
        /// <summary>Modo dos upscalers de fabricante (DLSS/FSR 4/XeSS): 0 nativo (DLAA/Native AA), 1 qualidade,
        /// 2 equilibrado, 3 desempenho, 4 ultra desempenho. É o SDK que decide a resolução interna.</summary>
        public int UpscalerQuality = 1;
        /// <summary>Qualidade do ray tracing da iluminação global: 0 rápido, 1 equilibrado, 2 qualidade (amostras e denoiser).</summary>
        public int GlobalIlluminationQuality = 1;
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
                    Bloom = true; AmbientOcclusion = false; DepthOfField = false; RenderScale = 0.77f; Upscaler = 2; GlobalIllumination = false;
                    // Com o upscaler do fabricante da placa, o modo Desempenho dá mais FPS com melhor imagem que STP a 77%.
                    int vendor = UpscalerController.VendorUpscaler();
                    if (vendor >= 0) { Upscaler = vendor; UpscalerQuality = 3; }
                    break;
                case 1:
                    TextureQuality = 2; Anisotropic = 2; Shadows = 2; Effects = 1; PostProcessing = 2; AntiAliasing = 2;
                    Bloom = true; AmbientOcclusion = false; DepthOfField = false; RenderScale = 1f; Upscaler = 0; GlobalIllumination = false;
                    break;
                case 2:
                    TextureQuality = 3; Anisotropic = 3; Shadows = 3; Effects = 2; PostProcessing = 3; AntiAliasing = 2;
                    Bloom = true; AmbientOcclusion = true; DepthOfField = false; RenderScale = 1f; Upscaler = 0; GlobalIllumination = false;
                    break;
                case 3:
                    TextureQuality = 3; Anisotropic = 4; Shadows = 4; Effects = 3; PostProcessing = 3; AntiAliasing = 5;
                    Bloom = true; AmbientOcclusion = true; DepthOfField = false; RenderScale = 1f; Upscaler = 0; GlobalIllumination = true;
                    GlobalIlluminationQuality = 2;
                    // Ultra com RT: o upscaler do fabricante em modo Qualidade reconstrói melhor em movimento que o TAA.
                    int best = UpscalerController.VendorUpscaler();
                    if (best >= 0) { Upscaler = best; UpscalerQuality = 1; }
                    break;
            }
        }

        /// <summary>Versão do formato guardado. 2: o cel shading passou a ser opcional (desligado por omissão).</summary>
        public const int CurrentVersion = 2;
        public int Version = CurrentVersion;

        public string ToJson() => JsonUtility.ToJson(this);
        public static GraphicsOptions FromJson(string json)
        {
            var o = new GraphicsOptions();
            if (!string.IsNullOrEmpty(json))
            {
                try { JsonUtility.FromJsonOverwrite(json, o); }
                catch (Exception) { o = new GraphicsOptions(); }
                // Preferências antigas guardavam sempre o cel shading que era o padrão (1) sem o jogador o ter escolhido:
                // migra para o novo padrão (renderização normal). Depois disto, a escolha do jogador é respeitada.
                if (!json.Contains("\"Version\"") || o.Version < 2) o.CelShading = 0;
                // Antes do modo próprio, o preset do upscaler vinha da escala de render.
                if (!json.Contains("\"UpscalerQuality\"")) o.UpscalerQuality = Mathf.Max(1, UpscalerController.QualityForScale(o.RenderScale));
                o.UpscalerQuality = Mathf.Clamp(o.UpscalerQuality, 0, UpscalerController.QualityNames.Length - 1);
                o.GlobalIlluminationQuality = Mathf.Clamp(o.GlobalIlluminationQuality, 0, 2);
                o.Version = CurrentVersion;
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

        public static bool DlssAvailable => UpscalerController.Available(3);
        public static string DlssStatus => DlssAvailable ? "DLSS 4.5 disponível" : "DLSS indisponível neste sistema";

        public static void Initialize(Camera camera, GraphicsOptions options)
        {
            Options = options;
            var current = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;
            if (current is UniversalRenderPipelineAsset urp && s_asset == null)
            {
                s_original = QualitySettings.renderPipeline;
                s_asset = UnityEngine.Object.Instantiate(urp);
                s_asset.name = urp.name + " (runtime)";
                UpscalerController.Initialize(s_asset, options);
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
            // Iluminação global (só conta com a feature do renderer activa): substitui a luz ambiente fixa pela luz
            // dos candeeiros/néons a ressaltar; intensidade calibrada para a sala ficar tão clara como sem ela.
            var gi = s_profile.Add<SurfaceCacheGIVolumeOverride>(true);
            gi.enabled.Override(true);
            gi.intensity.Override(4f);
            gi.lightTransportMultiBounce.Override(true);
            ConfigureGlobalIllumination(gi, Options.GlobalIlluminationQuality);
        }

        /// <summary>
        /// Ray tracing da GI + denoiser do Surface Cache. A convergência lenta em movimento vinha de poucas amostras por
        /// frame compensadas por muito filtro temporal (suavização 0,55 + pós-filtro temporal) e de um volume de 128 m
        /// (patches grandes demais para uma sala de 15 × 13 m). Agora: mais amostras e aquecimento forte nas superfícies
        /// recém-reveladas, menos histórico (responde a luz/objectos em movimento), filtro espacial curto (não borra a
        /// mesa com a parede) e denoise em ecrã a compensar o ruído. O pós-filtro temporal só fica na opção "qualidade".
        /// </summary>
        static void ConfigureGlobalIllumination(SurfaceCacheGIVolumeOverride gi, int quality)
        {
            quality = Mathf.Clamp(quality, 0, 2);
            int[] samples = { 3, 6, 8 };
            int[] warmup = { 4, 8, 12 };
            float[] temporal = { 0.45f, 0.5f, 0.55f };
            int[] spatial = { 4, 6, 8 };
            int[] lookups = { 6, 10, 14 };
            int[] passes = { 2, 3, 3 };
            int[] resolution = { 32, 48, 64 };
            gi.lightTransportSampleCount.Override(samples[quality]);
            gi.lightTransportWarmupSampleMultiplier.Override(warmup[quality]);
            gi.lightTransportDefragCount.Override(8);
            gi.patchFilteringTemporalSmoothing.Override(temporal[quality]);
            gi.patchFilteringSpatialEnabled.Override(true);
            gi.patchFilteringSpatialSampleCount.Override(spatial[quality]);
            gi.patchFilteringSpatialRadius.Override(0.6f);
            gi.patchFilteringPostTemporalEnabled.Override(quality >= 2);
            gi.screenFilteringLookupSampleCount.Override(lookups[quality]);
            gi.screenFilteringDenoisingPassCount.Override(passes[quality]);
            // A sala cabe num volume de 24 m; mais resolução = patches pequenos que acompanham bolas e personagens.
            gi.volumeSize.Override(24f);
            gi.volumeCascadeCount.Override(3);
            gi.volumeResolution.Override(resolution[quality]);
            gi.volumeDistanceFallback.Override(true);
        }

        /// <summary>O Surface Cache GI precisa de compute shaders (ray tracing por hardware ou em compute).</summary>
        public static bool GlobalIlluminationSupported => SystemInfo.supportsComputeShaders && SystemInfo.graphicsShaderLevel >= 50;

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

        static ChannelMixer s_perceptionMixer;

        /// <summary>Gameplay effects are independent of optional graphics settings.</summary>
        public static void SetPerception(Camera camera, float myopia, float deuteranopia)
        {
            if (s_profile == null || s_dof == null) return;
            if (s_perceptionMixer == null || !s_profile.components.Contains(s_perceptionMixer))
                s_perceptionMixer = s_profile.Add<ChannelMixer>(true);
            s_perceptionMixer.active = deuteranopia > 0f;
            // Machado et al. (2009), full deuteranopia matrix in linear RGB, as percentages.
            s_perceptionMixer.redOutRedIn.Override(36.7322f);
            s_perceptionMixer.redOutGreenIn.Override(86.0646f);
            s_perceptionMixer.redOutBlueIn.Override(-22.7968f);
            s_perceptionMixer.greenOutRedIn.Override(28.0085f);
            s_perceptionMixer.greenOutGreenIn.Override(67.2501f);
            s_perceptionMixer.greenOutBlueIn.Override(4.7413f);
            s_perceptionMixer.blueOutRedIn.Override(-1.182f);
            s_perceptionMixer.blueOutGreenIn.Override(4.294f);
            s_perceptionMixer.blueOutBlueIn.Override(96.8881f);
            s_dof.active = myopia > 0f || Options.PostProcessing >= 2 && Options.DepthOfField;
            if (myopia > 0f)
            {
                s_dof.gaussianStart.Override(0.18f);
                s_dof.gaussianEnd.Override(1.1f);
            }
            s_dof.gaussianMaxRadius.Override(myopia > 0f ? 1.5f : 1f);
            if (camera != null)
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = myopia > 0f || deuteranopia > 0f ||
                    Options.PostProcessing > 0 || Options.AntiAliasing == 1 || Options.AntiAliasing == 2 ||
                    Options.AntiAliasing == 5 || UpscalerController.IsTemporal(Options.Upscaler);
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
            o.CelShading = Mathf.Clamp(o.CelShading, 0, 2);
            VisualFactory.ApplyStyle(o.CelShading);
            InkOutline.SetStyle(o.CelShading);

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
                s_asset.renderScale = Mathf.Clamp(o.RenderScale, 1f / 3f, 1f);
                s_asset.msaaSampleCount = UpscalerController.IsTemporal(o.Upscaler) ? 1 : o.AntiAliasing == 3 ? 2 : o.AntiAliasing == 4 ? 4 : 1;
#if !ENABLE_UPSCALER_FRAMEWORK
#pragma warning disable CS0618
                s_asset.upscalingFilter = o.Upscaler == 1 ? UpscalingFilterSelection.FSR : o.Upscaler == 2 ? UpscalingFilterSelection.STP : UpscalingFilterSelection.Linear;
#pragma warning restore CS0618
#endif
                UpscalerController.Apply();
            }
            if (ShadowLights != null)
                foreach (var light in ShadowLights)
                {
                    if (light == null) continue;
                    light.shadows = o.Shadows == 0 ? LightShadows.None : o.Shadows == 1 ? LightShadows.Hard : LightShadows.Soft;
                    if (Application.isPlaying && light.type != LightType.Directional)
                        light.GetUniversalAdditionalLightData().additionalLightsShadowResolutionTier =
                            o.Shadows >= 3 ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierHigh :
                            o.Shadows == 2 ? UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierMedium :
                            UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                }

            // Pós-processamento.
            bool post = o.PostProcessing > 0;
            if (camera != null)
            {
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = post || o.AntiAliasing == 1 || o.AntiAliasing == 2 || o.AntiAliasing == 5;
                data.antialiasing = o.AntiAliasing == 1 ? AntialiasingMode.FastApproximateAntialiasing :
                                    o.AntiAliasing == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing :
                                    o.AntiAliasing == 5 ? AntialiasingMode.TemporalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
                camera.allowMSAA = o.AntiAliasing == 3 || o.AntiAliasing == 4;
                if (UpscalerController.IsTemporal(o.Upscaler))
                {
                    data.renderPostProcessing = true;
                    data.antialiasing = AntialiasingMode.None;
                    camera.allowMSAA = false;
                }
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
                // GI is lighting, independent of the post-processing quality switch.
                if (s_profile.TryGet<SurfaceCacheGIVolumeOverride>(out var gi))
                {
                    gi.active = true;
                    ConfigureGlobalIllumination(gi, o.GlobalIlluminationQuality);
                }
            }
            foreach (var (feature, _) in s_features)
            {
                if (feature == null) continue;
                string kind = feature.GetType().Name;
                if (kind == "ScreenSpaceAmbientOcclusion")
                    feature.SetActive(o.AmbientOcclusion && o.PostProcessing >= 1);
                // Por nome: o tipo só existe nas plataformas com SURFACE_CACHE_SUPPORTED. Inactiva, a feature não faz
                // nada (activa com o volume desligado poria a luz indirecta a zero).
                else if (kind == "SurfaceCacheGIRendererFeature")
                    feature.SetActive(o.GlobalIllumination && GlobalIlluminationSupported);
            }

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
            {
                var native = Screen.currentResolution;
                Screen.SetResolution(native.width, native.height, mode);
            }
        }
    }
}
