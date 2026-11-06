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
#include "PlatformBase.h"
#include "XeSSRenderAPI.h"

#include <assert.h>
#include <math.h>
#include <vector>
#include "Unity/IUnityLog.h"

#include "XeSSSDK/inc/xess/xess.h"
#include "XeSSLog.h"

// --------------------------------------------------------------------------
// UnitySetInterfaces

static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType);

static IUnityInterfaces* s_UnityInterfaces = NULL;
static IUnityGraphics* s_Graphics = NULL;
static bool s_PluginLoaded = false;

extern "C" void	UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
	s_UnityInterfaces = unityInterfaces;
	XeSSLog::SetUnityLog(s_UnityInterfaces->Get<IUnityLog>());

	s_Graphics = s_UnityInterfaces->Get<IUnityGraphics>();
	s_Graphics->RegisterDeviceEventCallback(OnGraphicsDeviceEvent);
	
#if SUPPORT_VULKAN
	if (s_Graphics->GetRenderer() == kUnityGfxRendererNull)
	{
		void RenderAPI_Vulkan_OnPluginLoad(IUnityInterfaces*);
		RenderAPI_Vulkan_OnPluginLoad(unityInterfaces);
	}
#endif // SUPPORT_VULKAN

	// Run OnGraphicsDeviceEvent(initialize) manually on plugin load
	OnGraphicsDeviceEvent(kUnityGfxDeviceEventInitialize);

	s_PluginLoaded = true;

	XESS_UNITY_LOG("XeSS plugin load successful.");
}

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginUnload()
{
	s_Graphics->UnregisterDeviceEventCallback(OnGraphicsDeviceEvent);

	XeSSLog::SetUnityLog(NULL);
	s_PluginLoaded = false;
}

extern "C" bool UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API UnityPluginLoaded()
{
	return s_PluginLoaded;
}

#if UNITY_WEBGL
typedef void	(UNITY_INTERFACE_API * PluginLoadFunc)(IUnityInterfaces* unityInterfaces);
typedef void	(UNITY_INTERFACE_API * PluginUnloadFunc)();

extern "C" void	UnityRegisterRenderingPlugin(PluginLoadFunc loadPlugin, PluginUnloadFunc unloadPlugin);

extern "C" void UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RegisterPlugin()
{
	UnityRegisterRenderingPlugin(UnityPluginLoad, UnityPluginUnload);
}
#endif

// --------------------------------------------------------------------------
// GraphicsDeviceEvent


static XeSSRenderAPI* s_CurrentAPI = NULL;
static UnityGfxRenderer s_DeviceType = kUnityGfxRendererNull;


static void UNITY_INTERFACE_API OnGraphicsDeviceEvent(UnityGfxDeviceEventType eventType)
{
	// Create graphics API implementation upon initialization
	if (eventType == kUnityGfxDeviceEventInitialize)
	{
		assert(s_CurrentAPI == NULL);
		s_DeviceType = s_Graphics->GetRenderer();
		s_CurrentAPI = CreateRenderAPI(s_DeviceType);
	}

	// Let the implementation process the device related events
	if (s_CurrentAPI)
	{
		s_CurrentAPI->ProcessDeviceEvent(eventType, s_UnityInterfaces);
	}

	// Cleanup graphics API implementation upon shutdown
	if (eventType == kUnityGfxDeviceEventShutdown)
	{
		if (s_CurrentAPI)
		{
			delete s_CurrentAPI;
			s_CurrentAPI = NULL;
		}
		s_DeviceType = kUnityGfxRendererNull;
	}
}

XESS_PACK_B()
typedef struct _xess_event_params
{
	xess_context_handle_t hContext;
	xess_result_t result;
	void* pParams;
} xess_event_params;
XESS_PACK_E()

static void UNITY_INTERFACE_API OnXeSSInitializeEvent(int eventID, void* data)
{
	if (data)
	{
		xess_event_params* eventParams = (xess_event_params*)data;

		if (s_CurrentAPI == NULL)
		{
			eventParams->result = XESS_RESULT_ERROR_UNSUPPORTED;
			return;
		}

		// XeSS initialize
		eventParams->result = s_CurrentAPI->OnXeSSInitializeEvent(eventParams->hContext, eventParams->pParams);
	}
}

static void UNITY_INTERFACE_API OnXeSSExecuteEvent(int eventID, void* data)
{
	if (data)
	{
		xess_event_params* eventParams = (xess_event_params*)data;

		if (s_CurrentAPI == NULL)
		{
			eventParams->result = XESS_RESULT_ERROR_UNSUPPORTED;
			return;
		}

		// XeSS execution
		eventParams->result = s_CurrentAPI->OnXeSSExecuteEvent(eventParams->hContext, eventParams->pParams);
	}
}

static void UNITY_INTERFACE_API OnXeSSDestroyContextEvent(int eventID, void* data)
{
	if (data)
	{
		if (s_CurrentAPI == NULL)
			return;

		s_CurrentAPI->OnXeSSDestroyContextEvent((xess_context_handle_t)data);
	}
}

// --------------------------------------------------------------------------
// RenderingPlugin_xessCreateContext
extern "C" xess_result_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_xessCreateContext(xess_context_handle_t * phContext)
{
	if (s_CurrentAPI == NULL)
		return XESS_RESULT_ERROR_UNSUPPORTED;

	return s_CurrentAPI->XeSSCreateContext(phContext);
}

// --------------------------------------------------------------------------
// RenderingPlugin_xessBuildPipelines
extern "C" xess_result_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_xessBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags)
{
	if (s_CurrentAPI == NULL)
		return XESS_RESULT_ERROR_UNSUPPORTED;

	return s_CurrentAPI->XeSSBuildPipelines(hContext, blocking, initFlags);
}

// --------------------------------------------------------------------------
// RenderingPlugin_xessGetInitParams
extern "C" xess_result_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_xessGetInitParams(xess_context_handle_t hContext, void* pInitParams)
{
	if (s_CurrentAPI == NULL)
		return XESS_RESULT_ERROR_UNSUPPORTED;

	return s_CurrentAPI->XeSSGetInitParams(hContext, pInitParams);
}

// --------------------------------------------------------------------------
// RenderingPlugin_xessIsValid
extern "C" bool UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_xessIsValid(xess_context_handle_t hContext)
{
	if (s_CurrentAPI == NULL)
		return false;

	return s_CurrentAPI->XeSSIsValid(hContext);
}

// --------------------------------------------------------------------------
// RenderingPlugin_xessDestroy
extern "C" xess_result_t UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_xessDestroyContext(xess_context_handle_t hContext)
{
	if (s_CurrentAPI == NULL)
		return XESS_RESULT_ERROR_UNSUPPORTED;

	return s_CurrentAPI->OnXeSSDestroyContextEvent(hContext);
}

// --------------------------------------------------------------------------
// GetXeSSExecuteEventFunc
extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_GetXeSSInitializeEvent()
{
	return OnXeSSInitializeEvent;
}

// --------------------------------------------------------------------------
// GetXeSSExecuteEventFunc
extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_GetXeSSExecuteEventFunc()
{
	return OnXeSSExecuteEvent;
}

// --------------------------------------------------------------------------
// GetXeSSExecuteEventFunc
extern "C" UnityRenderingEventAndData UNITY_INTERFACE_EXPORT UNITY_INTERFACE_API RenderingPlugin_GetXeSSDestroyContextEvent()
{
	return OnXeSSDestroyContextEvent;
}

