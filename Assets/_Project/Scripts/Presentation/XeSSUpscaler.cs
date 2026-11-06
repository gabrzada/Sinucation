#if ENABLE_UPSCALER_FRAMEWORK
using System;
using Intel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace FriendSlop.Presentation
{
    public sealed class XeSSOptions : UpscalerOptions
    {
        public XeSS.xess_quality_settings_t quality = XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_QUALITY;
        /// <summary>Modos do menu (Nativo, Qualidade, Equilibrado, Desempenho, Ultra desempenho) → presets do XeSS.</summary>
        public static XeSS.xess_quality_settings_t QualityForMode(int mode) => mode <= 0 ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_AA :
            mode == 1 ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_QUALITY :
            mode == 2 ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_BALANCED :
            mode == 3 ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_PERFORMANCE : XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_ULTRA_PERFORMANCE;
        // XeSS selects a supported native resolution nearest the existing render-scale control.
        public static XeSS.xess_quality_settings_t QualityForScale(float scale) => scale >= .9f ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_AA :
            scale >= .72f ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_ULTRA_QUALITY_PLUS :
            scale >= .63f ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_ULTRA_QUALITY :
            scale >= .55f ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_QUALITY :
            scale >= .47f ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_BALANCED :
            scale >= .38f ? XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_PERFORMANCE :
            XeSS.xess_quality_settings_t.XESS_QUALITY_SETTING_ULTRA_PERFORMANCE;
    }

    public sealed class XeSSUpscaler : AbstractUpscaler
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        static void Register() => UpscalerRegistry.Register<XeSSUpscaler, XeSSOptions>("intel.xess", "Intel XeSS");

        bool _supported;
        Vector2Int _lastDisplay, _lastInput;
        XeSS.xess_quality_settings_t _lastQuality;
        public XeSSUpscaler()
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12) return;
            try { _supported = XeSS.IsFeatureSupported(); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        public override string upscalerId => "intel.xess";
        public override string name => "Intel XeSS";
        public override bool isSupportedOnDevice => _supported;
        public override bool isTemporal => true;
        public override bool supportsSharpening => false;
        public override bool hasQualityMode => true;
        public override IUpscalerContext CreateContext(UpscalerOptions options, Vector2Int size) => new Context((XeSSOptions)options, size);
        public override void CalculateJitter(int frame, float ratio, out Vector2 jitter, out bool allowScaling)
        {
            XeSS.Utility.GetJitterOffset(out float x, out float y, frame, 64);
            jitter = new Vector2(x, y);
            allowScaling = false;
        }
        public override UpscalerResolutionInfo GetResolutionInfo(Vector2Int display, UpscalerOptions options)
        {
            var quality = ((XeSSOptions)options).quality;
            if (_lastDisplay != display || _lastQuality != quality)
            {
                var query = new XeSS.xess_context_handle_t();
                Check(XeSS.xessCreateContext(ref query), "query context");
                try
                {
                    Check(XeSS.xessGetInputResolution(query, new XeSS.xess_2d_t((uint)display.x, (uint)display.y), quality, out var input), "input resolution");
                    _lastInput = new Vector2Int((int)input.x, (int)input.y);
                    _lastDisplay = display;
                    _lastQuality = quality;
                }
                finally { XeSS.xessDestroyContext(query); }
            }
            return UpscalerResolutionInfo.Fixed(_lastInput);
        }

        static void Check(XeSS.xess_result_t result, string operation)
        {
            if ((int)result < 0) throw new InvalidOperationException("XeSS " + operation + ": " + result);
        }

        sealed class Context : IUpscalerContext
        {
            public Vector2Int createdForDisplayResolution { get; }
            public int lastUsedFrame { get; set; }
            internal XeSS.xess_context_handle_t native = new XeSS.xess_context_handle_t();
            internal readonly XeSS.xess_quality_settings_t quality;
            internal bool initialized;
            internal uint flags;
            internal bool first = true;
            public Context(XeSSOptions options, Vector2Int size)
            {
                quality = options.quality;
                createdForDisplayResolution = size;
                Check(XeSS.xessCreateContext(ref native), "create context");
            }
            public bool IsValidForOptions(UpscalerOptions options) => options is XeSSOptions x && x.quality == quality;
            public void Cleanup(CommandBuffer cmd)
            {
                if (native.context == IntPtr.Zero) return;
                Check(XeSS.xessDestroyContext(native, true, cmd), "destroy context");
                native.context = IntPtr.Zero;
            }
        }

        sealed class PassData
        {
            public Context context;
            public TextureHandle color, depth, motion, output;
            public Vector2Int inputSize, outputSize;
            public Vector2 jitter, velocityScale;
            public uint flags;
            public bool reset;
        }

        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
        {
            var io = frameData.Get<UpscalingIO>();
            if (io.context is not Context context) return;
            var desc = graph.GetTextureDesc(io.cameraColor);
            desc.name = "XeSS output";
            desc.width = io.postUpscaleResolution.x;
            desc.height = io.postUpscaleResolution.y;
            desc.msaaSamples = MSAASamples.None;
            desc.enableRandomWrite = true;
            desc.clearBuffer = false;
            var output = graph.CreateTexture(desc);
            using (var builder = graph.AddUnsafePass<PassData>("Intel XeSS Super Resolution", out var data))
            {
                data.context = context;
                data.color = io.cameraColor; data.depth = io.cameraDepth; data.motion = io.motionVectorColor; data.output = output;
                data.inputSize = io.preUpscaleResolution; data.outputSize = io.postUpscaleResolution;
                data.jitter = io.subpixelJitter; data.reset = io.resetHistory;
                float sign = io.motionVectorDirection == UpscalingIO.MotionVectorDirection.PreviousFrameToCurrentFrame ? -1f : 1f;
                data.velocityScale = io.motionVectorDomain == UpscalingIO.MotionVectorDomain.NDC ? sign * (Vector2)io.motionVectorTextureSize : sign * Vector2.one;
                data.flags = (uint)XeSS.xess_init_flags_t.XESS_INIT_FLAG_ENABLE_AUTOEXPOSURE;
                if (io.invertedDepth) data.flags |= (uint)XeSS.xess_init_flags_t.XESS_INIT_FLAG_INVERTED_DEPTH;
                if (!io.hdrInput) data.flags |= (uint)XeSS.xess_init_flags_t.XESS_INIT_FLAG_LDR_INPUT_COLOR;
                if (io.jitteredMotionVectors) data.flags |= (uint)XeSS.xess_init_flags_t.XESS_INIT_FLAG_JITTERED_MV;
                if (io.motionVectorTextureSize == io.postUpscaleResolution) data.flags |= (uint)XeSS.xess_init_flags_t.XESS_INIT_FLAG_HIGH_RES_MV;
                builder.UseTexture(data.color); builder.UseTexture(data.depth); builder.UseTexture(data.motion); builder.UseTexture(output, AccessFlags.Write);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData p, UnsafeGraphContext ctx) =>
                {
                    var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                    var state = p.context;
                    if (!state.initialized || state.flags != p.flags)
                    {
                        Check(XeSS.xessInitialize(state.native, cmd, new XeSS.xess_init_params_t
                        {
                            outputResolution = new XeSS.xess_2d_t((uint)p.outputSize.x, (uint)p.outputSize.y),
                            qualitySetting = state.quality, initFlags = p.flags
                        }), "initialize");
                        state.initialized = true; state.flags = p.flags; state.first = true;
                    }
                    Check(XeSS.xessSetVelocityScale(state.native, p.velocityScale.x, p.velocityScale.y), "motion scale");
                    RTHandle color = p.color, depth = p.depth, motion = p.motion, result = p.output;
                    Check(XeSS.xessExecute(state.native, cmd, new XeSS.xess_execute_params_t
                    {
                        colorTexture = color.rt, depthTexture = depth.rt, velocityTexture = motion.rt, outputTexture = result.rt,
                        jitterOffsetX = p.jitter.x, jitterOffsetY = p.jitter.y, exposureScale = 1f,
                        inputWidth = (uint)p.inputSize.x, inputHeight = (uint)p.inputSize.y, resetHistory = state.first || p.reset ? 1u : 0u
                    }), "execute");
                    state.first = false;
                });
            }
            io.cameraColor = output;
        }
    }
}
#endif
