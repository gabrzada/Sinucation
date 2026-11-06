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

/// <summary>
/// Wrapper for Intel xess
/// </summary>
namespace Intel
{
    internal static class LibXeSSDebugWrapper
    {
        [DllImport("libxess", EntryPoint = "xessSelectNetworkModel")]
        public static extern XeSS.xess_result_t xessSelectNetworkModel(IntPtr hContext, XeSSDebug.xess_network_model_t network);

        [DllImport("libxess", EntryPoint = "xessStartDump")]
        public static extern XeSS.xess_result_t xessStartDump(IntPtr hContext, IntPtr dump_parameters);
    }

    public static class XeSSDebug
    {
        /// <summary>
        /// X<sup>e</sup>SS network type.
        /// </summary>
        public enum xess_network_model_t
        {
            XESS_NETWORK_MODEL_KPSS = 0,
            XESS_NETWORK_MODEL_SPLAT = 1,
        };

        /// <summary>
        /// X<sup>e</sup>SS dump bits.
        /// </summary>
        public enum xess_dump_element_bits_t
        {
            XESS_DUMP_INPUT_COLOR = 0x01,
            XESS_DUMP_INPUT_VELOCITY = 0x02,
            XESS_DUMP_INPUT_DEPTH = 0x04,
            XESS_DUMP_INPUT_EXPOSURE_SCALE = 0x08,
            XESS_DUMP_INPUT_RESPONSIVE_PIXEL_MASK = 0x10,
            XESS_DUMP_OUTPUT = 0x20,
            XESS_DUMP_HISTORY = 0x40,
            XESS_DUMP_EXECUTION_PARAMETERS = 0x80, ///< All parameters passed to xessExecute
            XESS_DUMP_ALL_INPUTS = XESS_DUMP_INPUT_COLOR | XESS_DUMP_INPUT_VELOCITY |
                                   XESS_DUMP_INPUT_DEPTH | XESS_DUMP_INPUT_EXPOSURE_SCALE |
                                   XESS_DUMP_INPUT_RESPONSIVE_PIXEL_MASK | XESS_DUMP_EXECUTION_PARAMETERS,
            XESS_DUMP_ALL = 0x7FFFFFFF,
        };

        /// <summary>
        /// X<sup>e</sup>SS dump parameters
        /// </summary>
        public struct xess_dump_parameters_t
        {
            public string path;
            /** Frame index. Will be used as start for frame sequence. */
            public UInt32 frame_idx;
            /** Frame count to dump. Few frames less may be dumped due to possible frames in flight in
             * application
             */
            public UInt32 frame_count;
            /** Bitset showing set of elements that must be dumped. Element will be dumped if it exist
             * and corrseponding bit is not 0. 
             * Since it's meaningless to call Dump with empty set value of 0 will mean DUMP_ALL_INPUTS
             */
            public UInt32 dump_elements_mask;
            /** In case of depth render target with TYPELESS format it is not always possible
             *  to interpret depth values during dumping process.
             */
        }

        internal struct xess_native_dump_parameters_t
        {
            public xess_native_dump_parameters_t(xess_dump_parameters_t dump_param)
            {
                path = IntPtr.Zero;
                frame_idx = dump_param.frame_idx;
                frame_count = dump_param.frame_count;
                dump_elements_mask = dump_param.dump_elements_mask;
            }

            public IntPtr path;
            /** Frame index. Will be used as start for frame sequence. */
            public UInt32 frame_idx;
            /** Frame count to dump. Few frames less may be dumped due to possible frames in flight in
             * application
             */
            public UInt32 frame_count;
            /** Bitset showing set of elements that must be dumped. Element will be dumped if it exist
             * and corrseponding bit is not 0. 
             * Since it's meaningless to call Dump with empty set value of 0 will mean DUMP_ALL_INPUTS
             */
            public UInt32 dump_elements_mask;
            /** In case of depth render target with TYPELESS format it is not always possible
             *  to interpret depth values during dumping process.
             */
        }

        /// <summary>
        /// Select network to be used by X<sup>e</sup>SS
        /// Selects network model to use by X<sup>e</sup>SS.
        /// After call to this function - X<sup>e</sup>SS init function *must* be called
        /// </summary>
        /// <param name="hContext">The X<sup>e</sup>SS context handle.</param>
        /// <param name="network">The network model.</param>
        /// <returns>X<sup>e</sup>SS return status code.</returns>
        public static XeSS.xess_result_t xessSelectNetworkModel(XeSS.xess_context_handle_t hContext, xess_network_model_t network)
        {
            return LibXeSSDebugWrapper.xessSelectNetworkModel(hContext.context, network);
        }

        /// <summary>
        /// Dumps sequence of frames to the provided folder
        /// Call to this function initiates a dump for selected elements.
        /// X<sup> e</sup>SS SDK uses RAM cache to reduce dump overhead.Application should provide reasonable
        /// value for @ref xess_dump_parameters_t::frame_count frames (about 50 megs needed per cached frame).
        /// To enable several dumps per run application should provide correct
        /// @ref xess_dump_parameters_t::frame_idx value.This value used as a start index for frame
        /// dumping.
        /// After call to this function - each call to @ref xessD3D12Execute will result in new frame dumped to
        /// RAM cache.
        /// After @ref xess_dump_parameters_t::frame_count frames application will be blocked on call to
        /// @ref xessD3D12Execute in order to save cached frames to disk.This operation can take long time.
        /// Repetetive calls to this function can result in XESS_RESULT_ERROR_OPERATION_IN_PROGRESS which means that
        /// frame dump is in progress.
        /// </summary>
        /// <param name="hContext">X<sup>e</sup>SS context</param>
        /// <param name="dump_parameters">dump configuration</param>
        /// <returns>operation status</returns>
        public static XeSS.xess_result_t xessStartDump(XeSS.xess_context_handle_t hContext, xess_dump_parameters_t dump_parameters)
        {
            xess_native_dump_parameters_t pDumpParams_native = new xess_native_dump_parameters_t(dump_parameters);
            IntPtr pDumpParams_internal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(xess_native_dump_parameters_t)));
            if (pDumpParams_internal != IntPtr.Zero)
            {
                pDumpParams_native.path = Marshal.StringToHGlobalAnsi(dump_parameters.path);
                Marshal.StructureToPtr(pDumpParams_native, pDumpParams_internal, false);
                XeSS.xess_result_t ret = LibXeSSDebugWrapper.xessStartDump(hContext.context, pDumpParams_internal);
                Marshal.FreeHGlobal(pDumpParams_native.path);
                Marshal.FreeHGlobal(pDumpParams_internal);
                return ret;
            }
            return XeSS.xess_result_t.XESS_RESULT_ERROR_CSHARP_WRAPPER;
        }
    }
}