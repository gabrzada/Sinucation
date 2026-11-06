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
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

namespace Intel
{
    public static class XeSSD3D12
    {
        internal class Processer_D3D12 : XeSS.Processer
        {
            internal override XeSS.xess_result_t GetIntelXeFXVersion(IntPtr hContext, IntPtr pVersion)
            {
                return LibXeSSWrapper.xessGetIntelXeFXVersion(hContext, pVersion);
            }
            internal override XeSS.xess_result_t GetProperties(IntPtr hContext, IntPtr pOutputResolution, IntPtr pBindingProperties)
            {
                return LibXeSSWrapper.xessGetProperties(hContext, pOutputResolution, pBindingProperties);
            }
            internal override XeSS.xess_result_t GetInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolution)
            {
                return LibXeSSWrapper.xessGetInputResolution(hContext, pOutputResolution, qualitySettings, pInputResolution);
            }
            internal override XeSS.xess_result_t GetOptimalInputResolution(IntPtr hContext, IntPtr pOutputResolution, XeSS.xess_quality_settings_t qualitySettings, IntPtr pInputResolutionOptimal, IntPtr pInputResolutionMin, IntPtr pInputResolutionMax)
            {
                return LibXeSSWrapper.xessGetOptimalInputResolution(hContext, pOutputResolution, qualitySettings, pInputResolutionOptimal, pInputResolutionMin, pInputResolutionMax);
            }
            internal override XeSS.xess_result_t GetJitterScale(IntPtr hContext, IntPtr pX, IntPtr pY)
            {
                return LibXeSSWrapper.xessGetJitterScale(hContext, pX, pY);
            }
            internal override XeSS.xess_result_t GetVelocityScale(IntPtr hContext, IntPtr pX, IntPtr pY)
            {
                return LibXeSSWrapper.xessGetVelocityScale(hContext, pX, pY);
            }
            internal override XeSS.xess_result_t SetJitterScale(IntPtr hContext, float x, float y)
            {
                return LibXeSSWrapper.xessSetJitterScale(hContext, x, y);
            }
            internal override XeSS.xess_result_t SetVelocityScale(IntPtr hContext, float x, float y)
            {
                return LibXeSSWrapper.xessSetVelocityScale(hContext, x, y);
            }
            internal override XeSS.xess_result_t SetLoggingCallback(IntPtr hContext, XeSS.xess_logging_level_t loggingLevel, IntPtr loggingCallback)
            {
                return LibXeSSWrapper.xessSetLoggingCallback(hContext, loggingLevel, loggingCallback);
            }
            internal override XeSS.xess_result_t IsOptimalDriver(IntPtr hContext)
            {
                return LibXeSSWrapper.xessIsOptimalDriver(hContext);
            }
            internal override XeSS.xess_result_t GetInitParams(XeSS.xess_context_handle_t hContext, out XeSS.xess_init_params_t pInitParams)
            {
                IntPtr pInitParams_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_native_init_params_t)));
                if (pInitParams_internal != IntPtr.Zero)
                {
                    XeSS.xess_result_t ret = LibXeSSUnityPluginWrapper.RenderingPlugin_xessGetInitParams(hContext.context, pInitParams_internal);
                    xess_native_init_params_t pInitParams_native = (xess_native_init_params_t)Marshal.PtrToStructure(pInitParams_internal, typeof(xess_native_init_params_t));
                    pInitParams = new XeSS.xess_init_params_t();
                    pInitParams_native.to_xess_init_params_t(pInitParams);
                    Marshal.FreeHGlobal(pInitParams_internal);
                    return ret;
                }
                pInitParams = new XeSS.xess_init_params_t();
                return XeSS.xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
            }
            internal override XeSS.xess_result_t Initialize(XeSS.xess_context_handle_t hContext, CommandBuffer commandBuffer, XeSS.xess_init_params_t pInitParams)
            {
                if (commandBuffer != null)
                {
                    hContext.AllocEventParam(0, Marshal.SizeOf(typeof(xess_native_init_params_t)));
                    if (hContext.pEventParams_internal[0] != IntPtr.Zero && hContext.pNativeParams_internal[0] != IntPtr.Zero)
                    {
                        // construct init params
                        xess_native_init_params_t pNativeInitParams = new xess_native_init_params_t(pInitParams);
                        Marshal.StructureToPtr(pNativeInitParams, hContext.pNativeParams_internal[0], false);

                        // construct init event params
                        XeSS.xess_event_params_t pInitEventParams = new XeSS.xess_event_params_t();
                        pInitEventParams.pParams = hContext.pNativeParams_internal[0];
                        pInitEventParams.hContext = hContext.context;
                        pInitEventParams.result = XeSS.xess_result_t.XESS_RESULT_ERROR_UNKNOWN;
                        Marshal.StructureToPtr(pInitEventParams, hContext.pEventParams_internal[0], false);

                        // xess init
                        commandBuffer.IssuePluginEventAndData(LibXeSSUnityPluginWrapper.RenderingPlugin_GetXeSSInitializeEvent(), 1, hContext.pEventParams_internal[0]);

                        return XeSS.xess_result_t.XESS_RESULT_SUCCESS;
                    }
                    return XeSS.xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
                }

                return XeSS.xess_result_t.XESS_RESULT_ERROR_CSHARP_INVALID_ARGUMENT;
            }
            internal override XeSS.xess_result_t Execute(XeSS.xess_context_handle_t hContext, CommandBuffer commandBuffer, XeSS.xess_execute_params_t pExecParams)
            {
                hContext.AllocEventParam(1, Marshal.SizeOf(typeof(xess_native_execute_params_t)));

                if (hContext.pEventParams_internal[1] != IntPtr.Zero && hContext.pNativeParams_internal[1] != IntPtr.Zero)
                {
                    // construct exec params
                    xess_native_execute_params_t pNativeExecParams = new xess_native_execute_params_t(pExecParams);
                    Marshal.StructureToPtr(pNativeExecParams, hContext.pNativeParams_internal[1], false);

                    // construct event params
                    XeSS.xess_event_params_t pExecEventParams = new XeSS.xess_event_params_t();
                    pExecEventParams.pParams = hContext.pNativeParams_internal[1];
                    pExecEventParams.hContext = hContext.context;
                    pExecEventParams.result = XeSS.xess_result_t.XESS_RESULT_ERROR_UNKNOWN;
                    Marshal.StructureToPtr(pExecEventParams, hContext.pEventParams_internal[1], false);

                    // xess execute
                    if (XeSS.XeSSSampler != null)
                    {
                        commandBuffer.BeginSample(XeSS.XeSSSampler);
                    }
                    commandBuffer.IssuePluginEventAndData(LibXeSSUnityPluginWrapper.RenderingPlugin_GetXeSSExecuteEventFunc(), 1, hContext.pEventParams_internal[1]);
                    if (XeSS.XeSSSampler != null)
                    {
                        commandBuffer.EndSample(XeSS.XeSSSampler);
                    }

                    return XeSS.xess_result_t.XESS_RESULT_SUCCESS;
                }
                return XeSS.xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
            }
        }

        internal struct xess_native_execute_params_t
        {
            public xess_native_execute_params_t(XeSS.xess_execute_params_t pExecParams)
            {
                pColorTexture = pExecParams.colorTexture.GetNativeTexturePtr();
                pVelocityTexture = pExecParams.velocityTexture.GetNativeTexturePtr();
                pDepthTexture = pExecParams.depthTexture.GetNativeTexturePtr();
                pExposureScaleTexture = pExecParams.exposureScaleTexture != null ? pExecParams.exposureScaleTexture.GetNativeTexturePtr() : IntPtr.Zero;
                pResponsivePixelMaskTexture = pExecParams.responsivePixelMaskTexture != null ? pExecParams.responsivePixelMaskTexture.GetNativeTexturePtr() : IntPtr.Zero;
                pOutputTexture = pExecParams.outputTexture.GetNativeTexturePtr();

                jitterOffsetX = pExecParams.jitterOffsetX;
                jitterOffsetY = pExecParams.jitterOffsetY;
                exposureScale = pExecParams.exposureScale;
                resetHistory = pExecParams.resetHistory;
                inputWidth = pExecParams.inputWidth;
                inputHeight = pExecParams.inputHeight;
                inputColorBase = pExecParams.inputColorBase;
                inputMotionVectorBase = pExecParams.inputMotionVectorBase;
                inputDepthBase = pExecParams.inputDepthBase;
                inputResponsiveMaskBase = pExecParams.inputResponsiveMaskBase;
                reserved0 = pExecParams.reserved0;
                outputColorBase = pExecParams.outputColorBase;

                pDescriptorHeap = IntPtr.Zero;
                descriptorHeapOffset = descriptorHeapOffset = 0;
            }

            /** Input color texture. Must be in NON_PIXEL_SHADER_RESOURCE state.*/
            public IntPtr pColorTexture;
            /** Input motion vector texture. Must be in NON_PIXEL_SHADER_RESOURCE state.*/
            public IntPtr pVelocityTexture;
            /** Optional depth texture. Required if XESS_INIT_FLAG_HIGH_RES_MV has not been specified.
             * Must be in NON_PIXEL_SHADER_RESOURCE state.*/
            public IntPtr pDepthTexture;
            /** Optional 1x1 exposure scale texture. Required if XESS_INIT_FLAG_EXPOSURE_TEXTURE has been
             * specified. Must be in NON_PIXEL_SHADER_RESOURCE state */
            public IntPtr pExposureScaleTexture;
            /** Optional responsive pixel mask texture. Required if XESS_INIT_FLAG_RESPONSIVE_PIXEL_MASK
             * has been specified. Must be in NON_PIXEL_SHADER_RESOURCE state */
            public IntPtr pResponsivePixelMaskTexture;
            /** Output texture in target resolution. Must be in UNORDERED_ACCESS state.*/
            public IntPtr pOutputTexture;

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
            /** Optional external descriptor heap. */
            IntPtr pDescriptorHeap;
            /** Offset in external descriptor heap in bytes.*/
            UInt32 descriptorHeapOffset;
        };

        internal struct xess_native_init_params_t
        {
            public xess_native_init_params_t(XeSS.xess_init_params_t pInitParams)
            {
                outputResolution = pInitParams.outputResolution;
                qualitySetting = pInitParams.qualitySetting;
                initFlags = pInitParams.initFlags;
                creationNodeMask = pInitParams.creationNodeMask;
                visibleNodeMask = pInitParams.visibleNodeMask;
                pTempBufferHeap = IntPtr.Zero;
                bufferHeapOffset = bufferHeapOffset = 0;
                pTempTextureHeap = IntPtr.Zero;
                textureHeapOffset = textureHeapOffset = 0;
                pPipelineLibrary = IntPtr.Zero;
            }

            public void to_xess_init_params_t(XeSS.xess_init_params_t pInitParams)
            {
                pInitParams.outputResolution = outputResolution;
                pInitParams.qualitySetting = qualitySetting;
                pInitParams.initFlags = initFlags;
                pInitParams.creationNodeMask = creationNodeMask;
                pInitParams.visibleNodeMask = visibleNodeMask;
            }

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
            /** Optional externally allocated buffer storage for X<sup>e</sup>SS. If NULL the
            * storage is allocated internally. If allocated, the heap type must be
             * D3D12_HEAP_TYPE_DEFAULT. This heap is not accessed by the CPU. */
            IntPtr pTempBufferHeap;
            /** Offset in the externally allocated heap for temporary buffer storage. */
            UInt64 bufferHeapOffset;
            /** Optional externally allocated texture storage for X<sup>e</sup>SS. If NULL the
             * storage is allocated internally. If allocated, the heap type must be
             * D3D12_HEAP_TYPE_DEFAULT. This heap is not accessed by the CPU. */
            IntPtr pTempTextureHeap;
            /** Offset in the externally allocated heap for temporary texture storage. */
            UInt64 textureHeapOffset;
            /** Pointer to pipeline library. If not NULL will be used for pipeline caching. */
            IntPtr pPipelineLibrary;
        };
    }
}
