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
#pragma once

#include "Unity/IUnityGraphics.h"

#include <stddef.h>
#include <vector>
#include <mutex>
#include <Windows.h>

#include "XeSSSDK/inc/xess/xess.h"
#include "XeSSSDK/inc/xess/xess_debug.h"

typedef xess_result_t(*FP_xessDestroyContext)(xess_context_handle_t hContext);

struct IUnityInterfaces;

class ContextState
{
public:
	ContextState(xess_context_handle_t context)
		: hContext(context)
		, m_InflightIndex(0)
	{}
	virtual~ContextState() {}

	xess_context_handle_t GetContext() { return hContext; }

	static const int MAX_IN_FLIGHT = 10;

	void IncrementInFlightIndex()
	{  
		m_InflightIndex++;
		if (m_InflightIndex >= MAX_IN_FLIGHT)
		{
			m_InflightIndex = 0;
		}
	}

protected:
	int m_InflightIndex;
	xess_context_handle_t hContext;
};

// Super-simple "graphics abstraction". This is nothing like how a proper platform abstraction layer would look like;
// all this does is a base interface for whatever our plugin sample needs. Which is only "draw some triangles"
// and "modify a texture" at this point.
//
// There are implementations of this base class for D3D9, D3D11, OpenGL etc.; see individual RenderAPI_* files.
class XeSSRenderAPI
{
public:
	XeSSRenderAPI() 
		: m_XeSSLib(NULL) 
		, m_FP_xessDestroyContext(NULL)
	{}
	virtual ~XeSSRenderAPI()
	{
		if (m_XeSSLib)
		{
			::FreeLibrary(m_XeSSLib);
			m_XeSSLib = NULL;
		}
		m_FP_xessDestroyContext = NULL;
	}

	// Process general event like initialization, shutdown, device loss/reset etc.
	virtual void ProcessDeviceEvent(UnityGfxDeviceEventType type, IUnityInterfaces* interfaces) = 0;

	// XeSS functions, called in main thread
	virtual xess_result_t XeSSCreateContext(xess_context_handle_t* phContext) = 0;
	virtual xess_result_t XeSSBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags) = 0;
	virtual xess_result_t XeSSGetInitParams(xess_context_handle_t hContext, void* pInitParams) = 0;
	virtual bool XeSSIsValid(xess_context_handle_t hContext);

	// XeSS rendering event, called in rendering/submission thread
	virtual xess_result_t OnXeSSInitializeEvent(xess_context_handle_t hContext, void* pInitParams) = 0;
	virtual xess_result_t OnXeSSExecuteEvent(xess_context_handle_t hContext, void* pExecParams) = 0;
	virtual xess_result_t OnXeSSDestroyContextEvent(xess_context_handle_t hContext);

protected:
	virtual bool ValidAndWaitCmdFinished(xess_context_handle_t hContext) = 0;

	int FindContextStateIndex(xess_context_handle_t hContext);
	void ReleaseContextsResource();

	bool LoadXeSSLibrary(bool isD3D11);

	std::mutex m_ContextStatesMutex;
	std::vector<ContextState*> m_ContextStates;

	HMODULE m_XeSSLib;

	// XeSS functions
	FP_xessDestroyContext m_FP_xessDestroyContext;
};

// Create a graphics API implementation instance for the given API type.
XeSSRenderAPI* CreateRenderAPI(UnityGfxRenderer apiType);

