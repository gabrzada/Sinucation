/*******************************************************************************
 * Copyright 2024 Intel Corporation
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files(the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions :
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 ******************************************************************************/
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

/// <summary>
/// Wrapper for Intel xess
/// </summary>
namespace Intel
{
    internal static class LibXeSSWrapper
    {
        [DllImport("libxess", EntryPoint = "xessGetVersion")]
        public static extern XeSS.xess_result_t xessGetVersion(IntPtr pVersion);

        [DllImport("libxess", EntryPoint = "xessGetIntelXeFXVersion")]
        public static extern XeSS.xess_result_t xessGetIntelXeFXVersion(IntPtr hContext, IntPtr pVersion);

        [DllImport("libxess", EntryPoint = "xessGetProperties")]
        public static extern XeSS.xess_result_t xessGetProperties(IntPtr hContext, IntPtr pOutputResolution, IntPtr pBindingProperties);

        [DllImport("libxess", EntryPoint = "xessGetInputResolution")]
        public static extern XeSS.xess_result_t xessGetInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolution);

        [DllImport("libxess", EntryPoint = "xessGetOptimalInputResolution")]
        public static extern XeSS.xess_result_t xessGetOptimalInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolutionOptimal, IntPtr pInputResolutionMin, IntPtr pInputResolutionMax);

        [DllImport("libxess", EntryPoint = "xessGetJitterScale")]
        public static extern XeSS.xess_result_t xessGetJitterScale(IntPtr hContext, IntPtr pX, IntPtr pY);

        [DllImport("libxess", EntryPoint = "xessGetVelocityScale")]
        public static extern XeSS.xess_result_t xessGetVelocityScale(IntPtr hContext, IntPtr pX, IntPtr pY);

        [DllImport("libxess", EntryPoint = "xessDestroyContext")]
        public static extern XeSS.xess_result_t xessDestroyContext(IntPtr hContext);

        [DllImport("libxess", EntryPoint = "xessSetJitterScale")]
        public static extern XeSS.xess_result_t xessSetJitterScale(IntPtr hContext, float x, float y);

        [DllImport("libxess", EntryPoint = "xessSetVelocityScale")]
        public static extern XeSS.xess_result_t xessSetVelocityScale(IntPtr hContext, float x, float y);

        [DllImport("libxess", EntryPoint = "xessSetLoggingCallback")]
        public static extern XeSS.xess_result_t xessSetLoggingCallback(IntPtr hContext, XeSS.xess_logging_level_t loggingLevel, IntPtr loggingCallback);

        [DllImport("libxess", EntryPoint = "xessIsOptimalDriver")]
        public static extern XeSS.xess_result_t xessIsOptimalDriver(IntPtr hContext);
    }

    internal static class LibXeSSD3D11Wrapper
    {
        [DllImport("libxess_dx11", EntryPoint = "xessGetVersion")]
        public static extern XeSS.xess_result_t xessGetVersion(IntPtr pVersion);

        [DllImport("libxess_dx11", EntryPoint = "xessGetIntelXeFXVersion")]
        public static extern XeSS.xess_result_t xessGetIntelXeFXVersion(IntPtr hContext, IntPtr pVersion);

        [DllImport("libxess_dx11", EntryPoint = "xessGetProperties")]
        public static extern XeSS.xess_result_t xessGetProperties(IntPtr hContext, IntPtr pOutputResolution, IntPtr pBindingProperties);

        [DllImport("libxess_dx11", EntryPoint = "xessGetInputResolution")]
        public static extern XeSS.xess_result_t xessGetInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolution);

        [DllImport("libxess_dx11", EntryPoint = "xessGetOptimalInputResolution")]
        public static extern XeSS.xess_result_t xessGetOptimalInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolutionOptimal, IntPtr pInputResolutionMin, IntPtr pInputResolutionMax);

        [DllImport("libxess_dx11", EntryPoint = "xessGetJitterScale")]
        public static extern XeSS.xess_result_t xessGetJitterScale(IntPtr hContext, IntPtr pX, IntPtr pY);

        [DllImport("libxess_dx11", EntryPoint = "xessGetVelocityScale")]
        public static extern XeSS.xess_result_t xessGetVelocityScale(IntPtr hContext, IntPtr pX, IntPtr pY);

        [DllImport("libxess_dx11", EntryPoint = "xessDestroyContext")]
        public static extern XeSS.xess_result_t xessDestroyContext(IntPtr hContext);

        [DllImport("libxess_dx11", EntryPoint = "xessSetJitterScale")]
        public static extern XeSS.xess_result_t xessSetJitterScale(IntPtr hContext, float x, float y);

        [DllImport("libxess_dx11", EntryPoint = "xessSetVelocityScale")]
        public static extern XeSS.xess_result_t xessSetVelocityScale(IntPtr hContext, float x, float y);

        [DllImport("libxess_dx11", EntryPoint = "xessSetLoggingCallback")]
        public static extern XeSS.xess_result_t xessSetLoggingCallback(IntPtr hContext, XeSS.xess_logging_level_t loggingLevel, IntPtr loggingCallback);

        [DllImport("libxess_dx11", EntryPoint = "xessIsOptimalDriver")]
        public static extern XeSS.xess_result_t xessIsOptimalDriver(IntPtr hContext);
    }

    internal static class LibXeSSUnityPluginWrapper
    {
        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_xessCreateContext")]
        public static extern XeSS.xess_result_t RenderingPlugin_xessCreateContext(IntPtr phContext);

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_xessBuildPipelines")]
        public static extern XeSS.xess_result_t RenderingPlugin_xessBuildPipelines(IntPtr pContext, bool blocking, UInt32 initFlags);

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_xessGetInitParams")]
        public static extern XeSS.xess_result_t RenderingPlugin_xessGetInitParams(IntPtr pContext, IntPtr pInitParams);

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_xessDestroyContext")]
        public static extern XeSS.xess_result_t RenderingPlugin_xessDestroyContext(IntPtr pContext);

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_GetXeSSInitializeEvent")]
        public static extern IntPtr RenderingPlugin_GetXeSSInitializeEvent();

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_GetXeSSDestroyContextEvent")]
        public static extern IntPtr RenderingPlugin_GetXeSSDestroyContextEvent();

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_GetXeSSExecuteEventFunc")]
        public static extern IntPtr RenderingPlugin_GetXeSSExecuteEventFunc();

        [DllImport("XeSSRenderingPlugin", EntryPoint = "UnityPluginLoaded")]
        public static extern bool UnityPluginLoaded();

        [DllImport("XeSSRenderingPlugin", EntryPoint = "RenderingPlugin_xessIsValid")]
        public static extern bool RenderingPlugin_xessIsValid(IntPtr hContext);
    };

    internal static class XeSSShaderIDs
    {
        // Shader resource views, i.e. read-only bindings
        internal static readonly int SrvInputColor = Shader.PropertyToID("r_xess_input_color_jittered");
        internal static readonly int SrvInputMotionVectors = Shader.PropertyToID("r_xess_input_motion_vectors");
        internal static readonly int SrvInputDepth = Shader.PropertyToID("r_xess_input_depth");
        internal static readonly int SrvInputExposure = Shader.PropertyToID("r_xess_input_exposure");

        internal static readonly int UavUpscaledOutput = Shader.PropertyToID("rw_xess_upscaled_output");
    };

    public static class XeSS
    {
        abstract internal class Processer
        {
            abstract internal xess_result_t GetIntelXeFXVersion(IntPtr hContext, IntPtr pVersion);
            abstract internal xess_result_t GetProperties(IntPtr hContext, IntPtr pOutputResolution, IntPtr pBindingProperties);
            abstract internal xess_result_t GetInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolution);
            abstract internal xess_result_t GetOptimalInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolutionOptimal, IntPtr pInputResolutionMin, IntPtr pInputResolutionMax);
            abstract internal xess_result_t GetJitterScale(IntPtr hContext, IntPtr pX, IntPtr pY);
            abstract internal xess_result_t GetVelocityScale(IntPtr hContext, IntPtr pX, IntPtr pY);
            abstract internal xess_result_t SetJitterScale(IntPtr hContext, float x, float y);
            abstract internal xess_result_t SetVelocityScale(IntPtr hContext, float x, float y);
            abstract internal xess_result_t SetLoggingCallback(IntPtr hContext, XeSS.xess_logging_level_t loggingLevel, IntPtr loggingCallback);
            abstract internal xess_result_t IsOptimalDriver(IntPtr hContext);
            abstract internal xess_result_t GetInitParams(XeSS.xess_context_handle_t hContext, out XeSS.xess_init_params_t pInitParams);
            abstract internal xess_result_t Initialize(XeSS.xess_context_handle_t hContext, CommandBuffer commandBuffer, XeSS.xess_init_params_t pInitParams);
            abstract internal xess_result_t Execute(XeSS.xess_context_handle_t hContext, CommandBuffer commandBuffer, XeSS.xess_execute_params_t pExecParams);
        }

        /// <summary>
        /// X<sup>e</sup>SS context. 
        /// </summary>
        public class xess_context_handle_t
        {
            public IntPtr context;

            internal Processer processer = null;

            // internal pointer for init/exec/destroy
            internal IntPtr[] pEventParams_internal = new IntPtr[2];
            internal IntPtr[] pNativeParams_internal = new IntPtr[2];

            internal void AllocEventParam(int idx, int size)
            {
                if (pEventParams_internal[idx] == IntPtr.Zero)
                {
                    pEventParams_internal[idx] = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_event_params_t)));
                }
                if (pNativeParams_internal[idx] == IntPtr.Zero)
                {
                    pNativeParams_internal[idx] = Marshal.AllocHGlobal(size);
                }
            }

            internal void FreeEventParams()
            {
                for (int i = 0; i < 2; i++)
                {
                    if (pEventParams_internal[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pEventParams_internal[i]);
                        pEventParams_internal[i] = IntPtr.Zero;
                    }

                    if (pNativeParams_internal[i] != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(pNativeParams_internal[i]);
                        pNativeParams_internal[i] = IntPtr.Zero;
                    }
                }
            }

            internal void InitEventParams()
            {
                for (int i = 0; i < 2; i++)
                {
                    pEventParams_internal[i] = IntPtr.Zero;
                    pNativeParams_internal[i] = IntPtr.Zero;
                }
            }

            public xess_context_handle_t()
            {
                context = IntPtr.Zero;
                InitEventParams();
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12)
                {
                    processer = new XeSSD3D12.Processer_D3D12();
                }
                else if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan)
                {
                    processer = new XeSSVK.Processer_VK();
                }
                else if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11)
                {
                    processer = new XeSSD3D11.Processer_D3D11();
                }
            }

            ~xess_context_handle_t()
            {
                FreeEventParams();
            }
        };

        /// <summary>
        /// Set profiler sample for XeSS execution.
        /// </summary>
        /// <param name="sampler">Sampler for XeSS execution time.</param>
        static public void SetProfilerSampler(CustomSampler sampler)
        {
            XeSSSampler = sampler;
        }
        internal static CustomSampler XeSSSampler = null;

        /// <summary>
        /// Check if the XeSS feature is supported.
        /// </summary>
        /// <returns>Whether the XeSS feature is supported.</returns>
        static public bool IsFeatureSupported()
        {
            // D3D12 and Vulkan are full supported, and D3D11 is only supported on intel hardware
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12 &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Vulkan &&
                !(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11 && SystemInfo.graphicsDeviceVendorID == 0x8086))
            {
                return false;
            }

            if (!XeSS.xessPluginLoaded())
            {
                return false;
            }

            XeSS.xess_context_handle_t tryCreateXeSSContext = new XeSS.xess_context_handle_t();
            XeSS.xess_result_t ret;
            if ((ret = XeSS.xessCreateContext(ref tryCreateXeSSContext)) != XeSS.xess_result_t.XESS_RESULT_SUCCESS)
            {
                return false;
            }
            XeSS.xessDestroyContext(tryCreateXeSSContext);

            return true;
        }

        /// <summary>
        /// X<sup>e</sup>SS version.
        /// X<sup>e</sup>SS uses major.minor.patch version format and Numeric 90+ scheme for development stage builds.
        /// </summary>
        public struct xess_version_t
        {
            public UInt16 major;
            public UInt16 minor;
            public UInt16 patch;
            public UInt16 reserved;
        };

        /// <summary>
        /// 2D variable.
        /// </summary>
        public struct xess_2d_t
        {
            public xess_2d_t(UInt32 _x, UInt32 _y)
            {
                x = _x;
                y = _y;
            }
            public UInt32 x;
            public UInt32 y;
        };

        /// <summary>
        /// 2D coordinates.
        /// </summary>
        public struct xess_coord_t
        {
            public xess_coord_t(UInt32 _x, UInt32 _y)
            {
                x = _x;
                y = _y;
            }
            public UInt32 x;
            public UInt32 y;
        };

        /// <summary>
        /// X<sup>e</sup>SS quality settings.
        /// </summary>
        public enum xess_quality_settings_t
        {
            XESS_QUALITY_SETTING_ULTRA_PERFORMANCE = 100,
            XESS_QUALITY_SETTING_PERFORMANCE = 101,
            XESS_QUALITY_SETTING_BALANCED = 102,
            XESS_QUALITY_SETTING_QUALITY = 103,
            XESS_QUALITY_SETTING_ULTRA_QUALITY = 104,
            XESS_QUALITY_SETTING_ULTRA_QUALITY_PLUS = 105,
            XESS_QUALITY_SETTING_AA = 106,
        };

        /// <summary>
        /// X<sup>e</sup>SS initialization flags.
        /// </summary>
        public enum xess_init_flags_t
        {
            XESS_INIT_FLAG_NONE = 0,
            /** Use motion vectors at target resolution. */
            XESS_INIT_FLAG_HIGH_RES_MV = 1 << 0,
            /** Use inverted (increased precision) depth encoding */
            XESS_INIT_FLAG_INVERTED_DEPTH = 1 << 1,
            /** Use exposure texture to scale input color. */
            XESS_INIT_FLAG_EXPOSURE_SCALE_TEXTURE = 1 << 2,
            /** Use responsive pixel mask texture. */
            XESS_INIT_FLAG_RESPONSIVE_PIXEL_MASK = 1 << 3,
            /** Use velocity in NDC */
            XESS_INIT_FLAG_USE_NDC_VELOCITY = 1 << 4,
            /** Use external descriptor heap */
            XESS_INIT_FLAG_EXTERNAL_DESCRIPTOR_HEAP = 1 << 5,
            /** Disable tonemapping for input and output */
            XESS_INIT_FLAG_LDR_INPUT_COLOR = 1 << 6,
            /** Remove jitter from input velocity*/
            XESS_INIT_FLAG_JITTERED_MV = 1 << 7,
            /** Enable automatic exposure calculation. */
            XESS_INIT_FLAG_ENABLE_AUTOEXPOSURE = 1 << 8
        };

        /// <summary>
        /// Properties for internal X<sup>e</sup>SS resources.
        /// </summary>
        public struct xess_properties_t
        {
            /** Required amount of descriptors for X<sup>e</sup>SS */
            public UInt32 requiredDescriptorCount;
            /** The heap size required by X<sup>e</sup>SS for temporary buffer storage. */
            public UInt64 tempBufferHeapSize;
            /** The heap size required by X<sup>e</sup>SS for temporary texture storage. */
            public UInt64 tempTextureHeapSize;
        };

        /// <summary>
        /// X<sup>e</sup>SS return codes.
        /// </summary>
        public enum xess_result_t
        {
            /** Warning. Folder to store dump data doesn't exist. Write operation skipped.*/
            XESS_RESULT_WARNING_NONEXISTING_FOLDER = 1,
            /** An old or outdated driver. */
            XESS_RESULT_WARNING_OLD_DRIVER = 2,
            /** X<sup>e</sup>SS operation was successful. */
            XESS_RESULT_SUCCESS = 0,
            /** X<sup>e</sup>SS not supported on the GPU. An SM 6.4 capable GPU is required. */
            XESS_RESULT_ERROR_UNSUPPORTED_DEVICE = -1,
            /** An unsupported driver. */
            XESS_RESULT_ERROR_UNSUPPORTED_DRIVER = -2,
            /** Execute called without initialization. */
            XESS_RESULT_ERROR_UNINITIALIZED = -3,
            /** Invalid argument such as descriptor handles. */
            XESS_RESULT_ERROR_INVALID_ARGUMENT = -4,
            /** Not enough available GPU memory. */
            XESS_RESULT_ERROR_DEVICE_OUT_OF_MEMORY = -5,
            /** Device function such as resource or descriptor creation. */
            XESS_RESULT_ERROR_DEVICE = -6,
            /** The function is not implemented */
            XESS_RESULT_ERROR_NOT_IMPLEMENTED = -7,
            /** Invalid context. */
            XESS_RESULT_ERROR_INVALID_CONTEXT = -8,
            /** Operation not finished yet. */
            XESS_RESULT_ERROR_OPERATION_IN_PROGRESS = -9,
            /** Operation not supported in current configuration. */
            XESS_RESULT_ERROR_UNSUPPORTED = -10,
            /** The library cannot be loaded. */
            XESS_RESULT_ERROR_CANT_LOAD_LIBRARY = -11,

            /** The c# wrapper error. */
            XESS_RESULT_ERROR_CSHARP_WRAPPER = -100,
            /** The invalid paramaters passed to c#. */
            XESS_RESULT_ERROR_CSHARP_INVALID_ARGUMENT = -101,

            /** Unknown internal failure */
            XESS_RESULT_ERROR_UNKNOWN = -1000,
        };

        /// <summary>
        /// X<sup>e</sup>SS logging level
        /// </summary>
        public enum xess_logging_level_t
        {
            XESS_LOGGING_LEVEL_DEBUG = 0,
            XESS_LOGGING_LEVEL_INFO = 1,
            XESS_LOGGING_LEVEL_WARNING = 2,
            XESS_LOGGING_LEVEL_ERROR = 3
        };

        /// <summary>
        /// Initialization parameters for X<sup>e</sup>SS.
        /// </summary>
        public struct xess_init_params_t
        {
            /** Output width and height. */
            public XeSS.xess_2d_t outputResolution;
            /** Quality setting */
            public XeSS.xess_quality_settings_t qualitySetting;
            /** Initialization flags. */
            public UInt32 initFlags;
            /** Specifies the node mask for internally created resources on
             * multi-adapter systems. */
            public UInt32 creationNodeMask;
            /** Specifies the node visibility mask for internally created resources
             * on multi-adapter systems. */
            public UInt32 visibleNodeMask;
        };

        /// <summary>
        /// Execution parameters for X<sup>e</sup>SS
        /// </summary>
        public struct xess_execute_params_t
        {
            /** Input color texture.*/
            public Texture colorTexture;
            /** Input motion vector texture.*/
            public Texture velocityTexture;
            /** Optional depth texture. Required if XESS_INIT_FLAG_HIGH_RES_MV has not been specified.*/
            public Texture depthTexture;
            /** Optional 1x1 exposure scale texture. Required if XESS_INIT_FLAG_EXPOSURE_TEXTURE has been
             * specified.*/
            public Texture exposureScaleTexture;
            /** Optional responsive pixel mask texture. Required if XESS_INIT_FLAG_RESPONSIVE_PIXEL_MASK
             * has been specified.*/
            public Texture responsivePixelMaskTexture;
            /** Output texture in target resolution.*/
            public Texture outputTexture;

            /** Jitter X coordinate in the range [-0.5, 0.5]. */
            public float jitterOffsetX;
            /** Jitter Y coordinate in the range [-0.5, 0.5]. */
            public float jitterOffsetY;
            /** Optional input color scaling. Default is 1. */
            public float exposureScale;
            /** Resets the history accumulation in this frame. */
            public UInt32 resetHistory;
            /** Input color width. */
            public UInt32 inputWidth;
            /** Input color height. */
            public UInt32 inputHeight;
            /** Base coordinate for the input color in the texture. Default is (0,0). */
            public XeSS.xess_coord_t inputColorBase;
            /** Base coordinate for the input motion vector in the texture.  Default is (0,0). */
            public XeSS.xess_coord_t inputMotionVectorBase;
            /** Base coordinate for the input depth in the texture. Default is (0,0). */
            public XeSS.xess_coord_t inputDepthBase;
            /** Base coordinate for the input responsive pixel mask in the texture. Default is (0,0). */
            public XeSS.xess_coord_t inputResponsiveMaskBase;
            /** Reserved parameter. */
            public XeSS.xess_coord_t reserved0;
            /** Base coordinate for the output color.  Default is (0,0). */
            public XeSS.xess_coord_t outputColorBase;
        };

        /// <summary>
        /// XeSS rendering event param for native plugin
        /// </summary>
        public struct xess_event_params_t
        {
            public IntPtr hContext;
            public XeSS.xess_result_t result;
            public IntPtr pParams;
        };

        /// <summary>
        /// A logging callback provided by the application.
        /// </summary>
        /// <param name="message">message</param>
        /// <param name="loggingLevel">login level</param>
        public delegate void xess_app_log_callback_t(string message, xess_logging_level_t loggingLevel);

        /// <summary>
        /// Gets the X<sup>e</sup>SS version. This is baked into the X<sup>e</sup>SS SDK release.
        /// </summary>
        /// <param name="pVersion">Returned X<sup>e</sup>SS version.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessGetVersion(out xess_version_t pVersion)
        {
            IntPtr pVersion_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_version_t)));
            if (pVersion_internal != IntPtr.Zero)
            {
                xess_result_t ret;
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11)
                    ret = LibXeSSD3D11Wrapper.xessGetVersion(pVersion_internal);
                else
                    ret = LibXeSSWrapper.xessGetVersion(pVersion_internal);
                pVersion = (xess_version_t)Marshal.PtrToStructure(pVersion_internal, typeof(xess_version_t));
                Marshal.FreeHGlobal(pVersion_internal);
                return ret;
            }
            pVersion.major = pVersion.minor = pVersion.patch = pVersion.reserved = 0;
            return xess_result_t.XESS_RESULT_ERROR_UNKNOWN;
        }

        /// <summary>
        /// Check if the XeSS unity native plugin loaded.
        /// </summary>
        /// <returns>Is the plugin loaded</returns>
        public static bool xessPluginLoaded()
        {
            return LibXeSSUnityPluginWrapper.UnityPluginLoaded();
        }

        /// <summary>
        /// Gets the version of the loaded Intel XeFX library. When running on Intel platforms ,
        /// this function will return the version of the loaded Intel XeFX library, for other
        /// platforms 0.0.0 will be returned.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pVersion">Returned Intel XeFX library version.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessGetIntelXeFXVersion(xess_context_handle_t hContext, out xess_version_t pVersion)
        {
            IntPtr pVersion_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_version_t)));
            if (pVersion_internal != IntPtr.Zero)
            {
                xess_result_t ret = hContext.processer.GetIntelXeFXVersion(hContext.context, pVersion_internal);
                pVersion = (xess_version_t)Marshal.PtrToStructure(pVersion_internal, typeof(xess_version_t));
                Marshal.FreeHGlobal(pVersion_internal);
                return ret;
            }
            pVersion.major = pVersion.minor = pVersion.patch = pVersion.reserved = 0;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Gets X<sup>e</sup>SS internal resources properties.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pOutputResolution">Output resolution to calculate properties for.</param>
        /// <param name="pBindingProperties">Returned properties.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessGetProperties(xess_context_handle_t hContext, xess_2d_t pOutputResolution, out xess_properties_t pBindingProperties)
        {
            IntPtr pBindingProperties_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_properties_t)));
            IntPtr pOutputResolution_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            if (pBindingProperties_internal != IntPtr.Zero && pOutputResolution_internal != IntPtr.Zero)
            {
                Marshal.StructureToPtr(pOutputResolution, pOutputResolution_internal, false);
                xess_result_t ret = hContext.processer.GetProperties(hContext.context, pOutputResolution_internal, pBindingProperties_internal);
                pBindingProperties = (xess_properties_t)Marshal.PtrToStructure(pBindingProperties_internal, typeof(xess_properties_t));
                Marshal.FreeHGlobal(pBindingProperties_internal);
                Marshal.FreeHGlobal(pOutputResolution_internal);
                return ret;
            }
            pBindingProperties.requiredDescriptorCount = 0;
            pBindingProperties.tempBufferHeapSize = 0;
            pBindingProperties.tempTextureHeapSize = 0;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Get the input resolution for a specified output resolution for a given quality setting.
        /// X<sup> e</sup>SS expects all the input buffers except motion vectors to be in the returned resolution.
        /// Motion vectors can be either in output resolution (XESS_INIT_FLAG_HIGH_RES_MV) or
        /// returned resolution (default).
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pOutputResolution">Output resolution to calculate input resolution for.</param>
        /// <param name="qualitySettings">Desired quality setting.</param>
        /// <param name="pInputResolution">Required input resolution.</param>
        /// <returns></returns>
        public static xess_result_t xessGetInputResolution(xess_context_handle_t hContext, xess_2d_t pOutputResolution, xess_quality_settings_t qualitySettings, out xess_2d_t pInputResolution)
        {
            IntPtr pInputResolution_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            IntPtr pOutputResolution_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            if (pInputResolution_internal != IntPtr.Zero && pOutputResolution_internal != IntPtr.Zero)
            {
                Marshal.StructureToPtr(pOutputResolution, pOutputResolution_internal, false);
                xess_result_t ret = hContext.processer.GetInputResolution(hContext.context, pOutputResolution_internal, qualitySettings, pInputResolution_internal); ;
                pInputResolution = (xess_2d_t)Marshal.PtrToStructure(pInputResolution_internal, typeof(xess_2d_t));
                Marshal.FreeHGlobal(pInputResolution_internal);
                Marshal.FreeHGlobal(pOutputResolution_internal);
                return ret;
            }
            pInputResolution.x = pInputResolution.y = 0;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Get the optimal input resolution and possible range for a specified output resolution for a given quality setting.
        /// X<sup> e</sup>SS expects all the input buffers except motion vectors to be in the returned resolution range 
        /// and all input buffers to be in the same resolution.
        /// Motion vectors can be either in output resolution (XESS_INIT_FLAG_HIGH_RES_MV) or
        /// in the same resolution as other input buffers (by default).
        /// @note Aspect ratio of the input resolution must be the same as for the output resoulution.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pOutputResolution">Output resolution to calculate input resolution for.</param>
        /// <param name="qualitySettings">Desired quality setting.</param>
        /// <param name="pInputResolutionOptimal">Optimal input resolution.</param>
        /// <param name="pInputResolutionMin">Required minimal input resolution.</param>
        /// <param name="pInputResolutionMax">Required maximal input resolution.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static XeSS.xess_result_t xessGetOptimalInputResolution(xess_context_handle_t hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, out xess_2d_t pInputResolutionOptimal, out xess_2d_t pInputResolutionMin, out xess_2d_t pInputResolutionMax)
        {
            IntPtr pInputResolutionOptional_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            IntPtr pInputResolutionMin_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            IntPtr pInputResolutionMax_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            IntPtr pOutputResolution_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_2d_t)));
            if (pInputResolutionOptional_internal != IntPtr.Zero && pOutputResolution_internal != IntPtr.Zero &&
                pInputResolutionMin_internal != IntPtr.Zero && pInputResolutionMax_internal != IntPtr.Zero)
            {
                Marshal.StructureToPtr(pOutputResolution, pOutputResolution_internal, false);
                xess_result_t ret = hContext.processer.GetOptimalInputResolution(hContext.context, pOutputResolution_internal, qualitySettings, pInputResolutionOptional_internal, pInputResolutionMin_internal, pInputResolutionMax_internal); ;
                pInputResolutionOptimal = (xess_2d_t)Marshal.PtrToStructure(pInputResolutionOptional_internal, typeof(xess_2d_t));
                pInputResolutionMin = (xess_2d_t)Marshal.PtrToStructure(pInputResolutionMin_internal, typeof(xess_2d_t));
                pInputResolutionMax = (xess_2d_t)Marshal.PtrToStructure(pInputResolutionMax_internal, typeof(xess_2d_t));
                Marshal.FreeHGlobal(pInputResolutionOptional_internal);
                Marshal.FreeHGlobal(pInputResolutionMin_internal);
                Marshal.FreeHGlobal(pInputResolutionMax_internal);
                Marshal.FreeHGlobal(pOutputResolution_internal);
                return ret;
            }
            pInputResolutionOptimal.x = pInputResolutionOptimal.y = 0;
            pInputResolutionMin.x = pInputResolutionMin.y = 0;
            pInputResolutionMax.x = pInputResolutionMax.y = 0;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Gets jitter scale value.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pX">Jitter scale pointer for the X axis.</param>
        /// <param name="pY">Jitter scale pointer for the Y axis.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessGetJitterScale(xess_context_handle_t hContext, out float pX, out float pY)
        {
            IntPtr pX_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(float)));
            IntPtr pY_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(float)));
            if (pX_internal != IntPtr.Zero && pY_internal != IntPtr.Zero)
            {
                xess_result_t ret = hContext.processer.GetJitterScale(hContext.context, pX_internal, pY_internal);
                pX = (float)Marshal.PtrToStructure(pX_internal, typeof(float));
                pY = (float)Marshal.PtrToStructure(pY_internal, typeof(float));
                Marshal.FreeHGlobal(pX_internal);
                Marshal.FreeHGlobal(pY_internal);
                return ret;
            }
            pX = pY = 1.0f;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Gets velocity scale value.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pX">Velocity scale pointer for the X axis.</param>
        /// <param name="pY">Velocity scale pointer for the Y axis.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessGetVelocityScale(xess_context_handle_t hContext, out float pX, out float pY)
        {
            IntPtr pX_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(float)));
            IntPtr pY_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(float)));
            if (pX_internal != IntPtr.Zero && pY_internal != IntPtr.Zero)
            {
                xess_result_t ret = hContext.processer.GetVelocityScale(hContext.context, pX_internal, pY_internal);
                pX = (float)Marshal.PtrToStructure(pX_internal, typeof(float));
                pY = (float)Marshal.PtrToStructure(pY_internal, typeof(float));
                Marshal.FreeHGlobal(pX_internal);
                Marshal.FreeHGlobal(pY_internal);
                return ret;
            }
            pX = pY = 1.0f;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Initiates pipeline build process
        /// This function can only be called between xessCreateContext and
        /// xessInit
        /// </summary>
        /// <param name="hContext"></param>
        /// <param name="blocking"></param>
        /// <param name="initFlags"></param>
        /// <returns></returns>
        public static xess_result_t xessBuildPipelines(xess_context_handle_t hContext, bool blocking, UInt32 initFlags)
        {
            return LibXeSSUnityPluginWrapper.RenderingPlugin_xessBuildPipelines(hContext.context, blocking, initFlags);
        }

        /// <summary>
        /// Create an X<sup>e</sup>SS context.
        /// </summary>
        /// <param name="hContext">Returned xess context handle.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessCreateContext(ref xess_context_handle_t hContext)
        {
            hContext.InitEventParams();
            IntPtr context_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(IntPtr)));
            if (context_internal != IntPtr.Zero)
            {
                xess_result_t ret = LibXeSSUnityPluginWrapper.RenderingPlugin_xessCreateContext(context_internal);
                hContext.context = (IntPtr)Marshal.PtrToStructure(context_internal, typeof(IntPtr));
                Marshal.FreeHGlobal(context_internal);
                return ret;
            }
            hContext.context = IntPtr.Zero;
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Destroys the X<sup>e</sup>SS context.
        /// May happen in main thread or rendering thread.
        /// This function makes sure the pending command list has completed
        /// Note: 
        /// If it is called in main thread, please make sure no pending command list is recorded in rendering thread by yourself
        /// Normally we use the function in main thread just for checking if the XeSS feature is supported.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup> SS context handle.</param>
        /// <param name="callInRenderingThread">If the XeSS function happens in rendering thread.</param>
        /// <param name="commandBuffer">Unity command buffer.</param>
        /// <returns>X<sup>e</sup> SS return status code</returns>
        public static xess_result_t xessDestroyContext(xess_context_handle_t hContext, bool callInRenderingThread = false, CommandBuffer commandBuffer = null)
        {
            hContext.FreeEventParams();
            if (!callInRenderingThread)
            {
                return LibXeSSUnityPluginWrapper.RenderingPlugin_xessDestroyContext(hContext.context);
            }
            else if (commandBuffer != null)
            {
                commandBuffer.IssuePluginEventAndData(LibXeSSUnityPluginWrapper.RenderingPlugin_GetXeSSDestroyContextEvent(), 1, hContext.context);
                return XeSS.xess_result_t.XESS_RESULT_SUCCESS;
            }
            return XeSS.xess_result_t.XESS_RESULT_ERROR_CSHARP_INVALID_ARGUMENT;
        }

        /// <summary>
        /// Sets jitter scale value
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="x">scale for the X axis</param>
        /// <param name="y">scale for the Y axis</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessSetJitterScale(xess_context_handle_t hContext, float x, float y)
        {
            return hContext.processer.SetJitterScale(hContext.context, x, y);
        }

        /// <summary>
        /// Sets velocity scale value
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="x">scale for the X axis</param>
        /// <param name="y">scale for the Y axis</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessSetVelocityScale(xess_context_handle_t hContext, float x, float y)
        {
            return hContext.processer.SetVelocityScale(hContext.context, x, y);
        }

        /// <summary>
        /// Sets logging callback
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="loggingLevel">Minimum logging level for logging callback.</param>
        /// <param name="loggingCallback">Logging callback</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static xess_result_t xessSetLoggingCallback(xess_context_handle_t hContext, xess_logging_level_t loggingLevel, xess_app_log_callback_t loggingCallback)
        {
            IntPtr loggingCallback_internal = Marshal.GetFunctionPointerForDelegate(loggingCallback);
            if (loggingCallback_internal != null)
            {
                return hContext.processer.SetLoggingCallback(hContext.context, loggingLevel, loggingCallback_internal);
            }
            return xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }

        /// <summary>
        /// Indicates if the installed driver supports best X<sup>e</sup>SS experience. 
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <returns>xessIsOptimalDriver returns XESS_RESULT_SUCCESS, or XESS_RESULT_WARNING_OLD_DRIVER
        /// if installed driver may result in degraded performance or visual quality.
        /// xessD3D12CreateContext(..) will return XESS_RESULT_ERROR_UNSUPPORTED_DRIVER if driver does
        /// not support X<sup> e</sup>SS at all.</returns>
        public static xess_result_t xessIsOptimalDriver(xess_context_handle_t hContext)
        {
            return hContext.processer.IsOptimalDriver(hContext.context);
        }

        /// <summary>
        /// Check if the context is a valid context.
        /// </summary>
        /// <param name="hContext"></param>
        /// <returns>If the context is a valid context. Context may be remodestroied in plugin by device removed.</returns>
        public static bool xessIsValid(xess_context_handle_t hContext)
        {
            return LibXeSSUnityPluginWrapper.RenderingPlugin_xessIsValid(hContext.context);
        }

        /// <summary>
        /// Get X<sup>e</sup>SS initialization parameters.
        /// This function will return @ref XESS_RESULT_ERROR_UNINITIALIZED if @ref xessD3D12Init has not been called.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="pInitParams">Returned initialization parameters.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static XeSS.xess_result_t xessGetInitParams(XeSS.xess_context_handle_t hContext, out XeSS.xess_init_params_t pInitParams)
        {
            return hContext.processer.GetInitParams(hContext, out pInitParams);
        }

        /// <summary>
        /// Initialize X<sup>e</sup>SS. 
        /// Add initialization event to rendering thread.
        /// In rendering thread:
        /// This is a blocking call that initializes X<sup>e</sup>SS and triggers internal
        /// resources allocation and JIT for the X<sup>e</sup> SS kernels.The user must ensure that
        /// any pending command lists are completed before re-initialization.When
        /// During initialization, X<sup> e</sup>SS can create staging buffers and copy queues to
        /// upload internal data.These will be destroyed at the end of initialization.
        /// @note X<sup> e</sup>SS supports devices starting from D3D12_RESOURCE_HEAP_TIER_1, which means
        /// that buffers and textures can not live in the same resource heap.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="commandBuffer">Unity command buffer.</param>
        /// <param name="pInitParams">Initialization parameters.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static XeSS.xess_result_t xessInitialize(XeSS.xess_context_handle_t hContext, CommandBuffer commandBuffer, XeSS.xess_init_params_t pInitParams)
        { 
            return hContext.processer.Initialize(hContext, commandBuffer, pInitParams);
        }

        /// <summary>
        /// Record X<sup>e</sup>SS upscaling commands into the command list.
        /// Add execution event to rendering thread.
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="commandBuffer">Unity command buffer.</param>
        /// <param name="pExecParams">Execution parameters.</param>
        /// <returns></returns>
        public static XeSS.xess_result_t xessExecute(XeSS.xess_context_handle_t hContext, CommandBuffer commandBuffer, XeSS.xess_execute_params_t pExecParams)
        {
            return hContext.processer.Execute(hContext, commandBuffer, pExecParams);
        }

        public static class Utility
        {
            static bool usingFixedJitterPhaseCount = true;
            const int fixedJitterPhaseCount = 64;

            public static void SetUseFixedJitterPhaseCount(bool isUsingFixedJitterPhaseCount)
            {
                usingFixedJitterPhaseCount = isUsingFixedJitterPhaseCount;
            }

            public static float GetMipmapBiasOffset(uint renderWidth, uint displayWidth)
            {
                return Mathf.Log((float)renderWidth / displayWidth, 2.0f);
            }

            public static int GetJitterPhaseCount(uint renderWidth, uint displayWidth)
            {
                if (!usingFixedJitterPhaseCount)
                {
                    const float basePhaseCount = 8.0f;
                    int jitterPhaseCount = (int)(basePhaseCount * Mathf.Pow((float)displayWidth / renderWidth, 2.0f));
                    return jitterPhaseCount;
                }
                return fixedJitterPhaseCount;
            }

            public static void GetJitterOffset(out float outX, out float outY, int index, int phaseCount)
            {
                outX = Halton((index % phaseCount) + 1, 2) - 0.5f;
                outY = Halton((index % phaseCount) + 1, 3) - 0.5f;
            }

            // Calculate halton number for index and base.
            private static float Halton(int index, int @base)
            {
                float f = 1.0f, result = 0.0f;

                for (int currentIndex = index; currentIndex > 0;)
                {

                    f /= @base;
                    result += f * (currentIndex % @base);
                    currentIndex = (int)Mathf.Floor((float)currentIndex / @base);
                }

                return result;
            }
        }
    }
}
