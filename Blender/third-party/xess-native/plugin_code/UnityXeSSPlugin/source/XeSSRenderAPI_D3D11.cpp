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
#include "XeSSRenderAPI.h"
#include "PlatformBase.h"

// Direct3D 11 implementation of RenderAPI.
#if SUPPORT_D3D11
#include <cmath>
#include <stdio.h>
#include <d3d11.h>
#include <dxgi.h>
#include "Unity/IUnityGraphicsD3D11.h"

#include "XeSSLog.h"
#include "XeSSSDK/inc/xess/xess_d3d11.h"

extern IUnityLog* s_UnityLogPtr;

typedef xess_result_t(*FP_xessD3D11CreateContext)(ID3D11Device* device, xess_context_handle_t* phContext);
typedef xess_result_t(*FP_xessD3D11GetInitParams)(xess_context_handle_t hContext, xess_d3d11_init_params_t* pInitParams);
typedef xess_result_t(*FP_xessD3D11Init)(xess_context_handle_t hContext, const xess_d3d11_init_params_t* pInitParams);
typedef xess_result_t(*FP_xessD3D11Execute)(xess_context_handle_t hContext, const xess_d3d11_execute_params_t* pExecParams);

class ContextState_D3D11 : public ContextState
{
public:
	ContextState_D3D11(xess_context_handle_t context) : ContextState(context)
	{}
	virtual ~ContextState_D3D11()
	{}
};

class XeSSRenderAPI_D3D11 : public XeSSRenderAPI
{
public:
	XeSSRenderAPI_D3D11();
	virtual ~XeSSRenderAPI_D3D11() { }

	virtual void ProcessDeviceEvent(UnityGfxDeviceEventType type, IUnityInterfaces* interfaces);

	// XeSS
	virtual xess_result_t XeSSCreateContext(xess_context_handle_t* phContext);
	virtual xess_result_t XeSSBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags);
	virtual xess_result_t XeSSGetInitParams(xess_context_handle_t hContext, void* pInitParams);

	// XeSS rendering events
	virtual xess_result_t OnXeSSInitializeEvent(xess_context_handle_t hContext, void* pInitParams);
	virtual xess_result_t OnXeSSExecuteEvent(xess_context_handle_t hContext, void* pExecParams);

protected:
	virtual bool ValidAndWaitCmdFinished(xess_context_handle_t hContext);

private:
	void ConfigPlugin(IUnityGraphicsD3D11* d3d11);
	void CreateResources();
	void ReleaseResources();

private:
	IUnityGraphicsD3D11* m_D3D11;

	// XeSS functions
	FP_xessD3D11CreateContext m_FP_xessD3D11CreateContext;
	FP_xessD3D11GetInitParams m_FP_xessD3D11GetInitParams;
	FP_xessD3D11Init m_FP_xessD3D11Init;
	FP_xessD3D11Execute m_FP_xessD3D11Execute;
};

XeSSRenderAPI_D3D11::XeSSRenderAPI_D3D11()
	: m_D3D11(NULL)
	, m_FP_xessD3D11CreateContext(NULL)
	, m_FP_xessD3D11GetInitParams(NULL)
	, m_FP_xessD3D11Init(NULL)
	, m_FP_xessD3D11Execute(NULL)
{
	if (LoadXeSSLibrary(true))
	{
		// load functions
		m_FP_xessD3D11CreateContext = (FP_xessD3D11CreateContext)GetProcAddress(m_XeSSLib, "xessD3D11CreateContext");
		m_FP_xessD3D11GetInitParams = (FP_xessD3D11GetInitParams)GetProcAddress(m_XeSSLib, "xessD3D11GetInitParams");
		m_FP_xessD3D11Init = (FP_xessD3D11Init)GetProcAddress(m_XeSSLib, "xessD3D11Init");
		m_FP_xessD3D11Execute = (FP_xessD3D11Execute)GetProcAddress(m_XeSSLib, "xessD3D11Execute");
	}
}

void XeSSRenderAPI_D3D11::ProcessDeviceEvent(UnityGfxDeviceEventType type, IUnityInterfaces* interfaces)
{
	switch (type)
	{
	case kUnityGfxDeviceEventInitialize:
	case kUnityGfxDeviceEventAfterReset:
		m_D3D11 = interfaces->Get<IUnityGraphicsD3D11>();
		if (m_D3D11 == NULL)
		{
			XESS_UNITY_LOG("IUnityGraphicsD3D11 device get failed!");
			break;
		}
		ConfigPlugin(m_D3D11);
		CreateResources();
		break;
	case kUnityGfxDeviceEventShutdown:
	case kUnityGfxDeviceEventBeforeReset:
		if (m_D3D11)
		{
			ReleaseResources();
			m_D3D11 = NULL;
		}
		break;
	}
}

// XeSS
xess_result_t XeSSRenderAPI_D3D11::XeSSCreateContext(xess_context_handle_t* phContext)
{
	if (!m_FP_xessD3D11CreateContext)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	ID3D11Device* device = m_D3D11->GetDevice();
	xess_result_t ret = m_FP_xessD3D11CreateContext(device, phContext);
	if (ret == XESS_RESULT_SUCCESS)
	{
		ContextState_D3D11* cs = new ContextState_D3D11(*phContext);
		m_ContextStates.push_back(cs);
	}
	return ret;
}

xess_result_t XeSSRenderAPI_D3D11::XeSSBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags)
{
	return XESS_RESULT_ERROR_NOT_IMPLEMENTED;
}

xess_result_t XeSSRenderAPI_D3D11::XeSSGetInitParams(xess_context_handle_t hContext, void* pInitParams)
{
	if(!m_FP_xessD3D11GetInitParams)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;
	return m_FP_xessD3D11GetInitParams(hContext, (xess_d3d11_init_params_t*)pInitParams);
}

// XeSS rendering events
xess_result_t XeSSRenderAPI_D3D11::OnXeSSInitializeEvent(xess_context_handle_t hContext, void* pInitParams)
{
	if (!m_FP_xessD3D11Init)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	if (!ValidAndWaitCmdFinished(hContext))
		return XESS_RESULT_ERROR_UNKNOWN;

	{
		std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
		int contexIdx = FindContextStateIndex(hContext);
		if (contexIdx < 0)
			return XESS_RESULT_ERROR_UNKNOWN;

		const xess_d3d11_init_params_t* pParams = (const xess_d3d11_init_params_t*)pInitParams;
		return m_FP_xessD3D11Init(hContext, pParams);
	}
}

xess_result_t XeSSRenderAPI_D3D11::OnXeSSExecuteEvent(xess_context_handle_t hContext, void* pExecParams)
{
	if (!m_FP_xessD3D11Execute)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
	int contexIdx = FindContextStateIndex(hContext);
	if (contexIdx < 0)
		return XESS_RESULT_ERROR_UNKNOWN;

	const xess_d3d11_execute_params_t* pParams = (const xess_d3d11_execute_params_t*)pExecParams;
	return m_FP_xessD3D11Execute(hContext, pParams);
}

bool XeSSRenderAPI_D3D11::ValidAndWaitCmdFinished(xess_context_handle_t hContext)
{
	return true;
}

void XeSSRenderAPI_D3D11::ConfigPlugin(IUnityGraphicsD3D11* d3d11)
{
	// need do nothing
}

void XeSSRenderAPI_D3D11::CreateResources()
{
	
}

void XeSSRenderAPI_D3D11::ReleaseResources()
{
	ReleaseContextsResource();
}

XeSSRenderAPI* CreateRenderAPI_D3D11()
{
	return new XeSSRenderAPI_D3D11();
}

#endif	// #if SUPPORT_D3D11