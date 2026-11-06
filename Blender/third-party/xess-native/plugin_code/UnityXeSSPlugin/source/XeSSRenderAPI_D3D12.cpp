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

// Direct3D 12 implementation of RenderAPI.
#if SUPPORT_D3D12
#include <cmath>
#include <stdio.h>
#include <d3d12.h>
#include <dxgi.h>
#include "Unity/IUnityGraphicsD3D12.h"

#include "XeSSLog.h"
#include "XeSSSDK/inc/xess/xess_d3d12.h"
#include "XeSSSDK/inc/xess/xess_d3d12_debug.h"

extern IUnityLog* s_UnityLogPtr;

typedef xess_result_t(*FP_xessD3D12CreateContext)(ID3D12Device* pDevice, xess_context_handle_t* phContext);
typedef xess_result_t(*FP_xessD3D12BuildPipelines)(xess_context_handle_t hContext, ID3D12PipelineLibrary* pPipelineLibrary, bool blocking, uint32_t initFlags);
typedef xess_result_t(*FP_xessD3D12GetInitParams)(xess_context_handle_t hContext, xess_d3d12_init_params_t* pInitParams);
typedef xess_result_t(*FP_xessD3D12Init)(xess_context_handle_t hContext, const xess_d3d12_init_params_t* pInitParams);
typedef xess_result_t(*FP_xessD3D12Execute)(xess_context_handle_t hContext, ID3D12GraphicsCommandList* pCommandList, const xess_d3d12_execute_params_t* pExecParams);

class ContextState_D3D12 : public ContextState
{
public:
	ContextState_D3D12(xess_context_handle_t context) : ContextState(context) 
	{
		fenceValue = 0;

		ID3D12Device* device = m_pD3D12->GetDevice();
		HRESULT hr = E_FAIL;
		const UINT kNodeMask = 0;
	}
	virtual ~ContextState_D3D12() 
	{
	}

	void SetFenceValue(UINT64 v) { fenceValue = v; }
	UINT64 GetFenceValue() { return fenceValue; }

	static void SetD3D12Graphics(IUnityGraphicsD3D12v8* d3d12) { m_pD3D12 = d3d12; }

protected:
	UINT64 fenceValue;

	static IUnityGraphicsD3D12v8* m_pD3D12;
};
IUnityGraphicsD3D12v8* ContextState_D3D12::m_pD3D12 = NULL;

class XeSSRenderAPI_D3D12 : public XeSSRenderAPI
{
public:
	XeSSRenderAPI_D3D12();
	virtual ~XeSSRenderAPI_D3D12() { }

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
	void ConfigPlugin(IUnityGraphicsD3D12v8* d3d12);
	void CreateResources();
	void ReleaseResources();

	void DebugPrintInitializeParams(const xess_d3d12_init_params_t* pParams);
	void DebugPrintExecuteParams(xess_d3d12_execute_params_t* pParams);

private:
	IUnityGraphicsD3D12v8* m_D3D12;

	// XeSS functions
	FP_xessD3D12CreateContext m_FP_xessD3D12CreateContext;
	FP_xessD3D12BuildPipelines m_FP_xessD3D12BuildPipelines;
	FP_xessD3D12GetInitParams m_FP_xessD3D12GetInitParams;
	FP_xessD3D12Init m_FP_xessD3D12Init;
	FP_xessD3D12Execute m_FP_xessD3D12Execute;
};

XeSSRenderAPI* CreateRenderAPI_D3D12()
{
	return new XeSSRenderAPI_D3D12();
}

XeSSRenderAPI_D3D12::XeSSRenderAPI_D3D12()
	: m_D3D12(NULL)
	, m_FP_xessD3D12CreateContext(NULL)
	, m_FP_xessD3D12BuildPipelines(NULL)
	, m_FP_xessD3D12GetInitParams(NULL)
	, m_FP_xessD3D12Init(NULL)
	, m_FP_xessD3D12Execute(NULL)
{
	if (LoadXeSSLibrary(false))
	{
		// load functions
		m_FP_xessD3D12CreateContext = (FP_xessD3D12CreateContext)GetProcAddress(m_XeSSLib, "xessD3D12CreateContext");
		m_FP_xessD3D12BuildPipelines = (FP_xessD3D12BuildPipelines)GetProcAddress(m_XeSSLib, "xessD3D12BuildPipelines");
		m_FP_xessD3D12GetInitParams = (FP_xessD3D12GetInitParams)GetProcAddress(m_XeSSLib, "xessD3D12GetInitParams");
		m_FP_xessD3D12Init = (FP_xessD3D12Init)GetProcAddress(m_XeSSLib, "xessD3D12Init");
		m_FP_xessD3D12Execute = (FP_xessD3D12Execute)GetProcAddress(m_XeSSLib, "xessD3D12Execute");
	}
}

void XeSSRenderAPI_D3D12::ConfigPlugin(IUnityGraphicsD3D12v8* d3d12)
{
	UnityD3D12PluginEventConfig config;
	config.graphicsQueueAccess = kUnityD3D12GraphicsQueueAccess_DontCare;
	config.flags = kUnityD3D12EventConfigFlag_FlushCommandBuffers;
	config.ensureActiveRenderTextureIsBound = false;
	d3d12->ConfigureEvent(1, &config);
}

void XeSSRenderAPI_D3D12::CreateResources()
{
	ID3D12Device* device = m_D3D12->GetDevice();
	ContextState_D3D12::SetD3D12Graphics(m_D3D12);
}

void XeSSRenderAPI_D3D12::ReleaseResources()
{
	ReleaseContextsResource();
	ContextState_D3D12::SetD3D12Graphics(NULL);
}

void XeSSRenderAPI_D3D12::ProcessDeviceEvent(UnityGfxDeviceEventType type, IUnityInterfaces* interfaces)
{
	switch (type)
	{
	case kUnityGfxDeviceEventInitialize:
	case kUnityGfxDeviceEventAfterReset:
		m_D3D12 = interfaces->Get<IUnityGraphicsD3D12v8>();
		if (m_D3D12 == NULL)
		{
			XESS_UNITY_LOG("IUnityGraphicsD3D12v8 device get failed!");
			break;
		}
		ConfigPlugin(m_D3D12);
		CreateResources();
		break;
	case kUnityGfxDeviceEventShutdown:
	case kUnityGfxDeviceEventBeforeReset:
		if (m_D3D12)
		{
			ReleaseResources();
			m_D3D12 = NULL;
		}
		break;
	}
}

// XeSS create context
xess_result_t XeSSRenderAPI_D3D12::XeSSCreateContext(xess_context_handle_t* phContext)
{
	if (!m_FP_xessD3D12CreateContext)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	ID3D12Device* device = m_D3D12->GetDevice();
	xess_result_t ret = m_FP_xessD3D12CreateContext(device, phContext);
	if (ret == XESS_RESULT_SUCCESS)
	{
		ContextState_D3D12 *cs = new ContextState_D3D12(*phContext);
		m_ContextStates.push_back(cs);
	}
	return ret;
}

// XeSS build pipelines
xess_result_t XeSSRenderAPI_D3D12::XeSSBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags)
{
	if (!m_FP_xessD3D12BuildPipelines)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;
	return m_FP_xessD3D12BuildPipelines(hContext, NULL, blocking, initFlags);
}

xess_result_t XeSSRenderAPI_D3D12::XeSSGetInitParams(xess_context_handle_t hContext, void* pInitParams)
{
	if (!m_FP_xessD3D12GetInitParams)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;
	return m_FP_xessD3D12GetInitParams(hContext, (xess_d3d12_init_params_t*)pInitParams);
}

void XeSSRenderAPI_D3D12::DebugPrintInitializeParams(const xess_d3d12_init_params_t* pParams)
{
	XESS_UNITY_LOG("XeSS Plugin: Init! Param:");
	XESS_UNITY_LOG_FORMAT("outputResolution:[%d, %d]", (int)pParams->outputResolution.x, (int)pParams->outputResolution.y);
	XESS_UNITY_LOG_FORMAT("initFlags:%d", pParams->initFlags);
	XESS_UNITY_LOG_FORMAT("pPipelineLibrary:%d", pParams->pPipelineLibrary);
	XESS_UNITY_LOG_FORMAT("bufferHeapOffset:%d", pParams->bufferHeapOffset);
	XESS_UNITY_LOG_FORMAT("creationNodeMask:%d", pParams->creationNodeMask);
	XESS_UNITY_LOG_FORMAT("textureHeapOffset:%d", pParams->textureHeapOffset);
	XESS_UNITY_LOG_FORMAT("visibleNodeMask:%d", pParams->visibleNodeMask);
	XESS_UNITY_LOG_FORMAT("pTempBufferHeap:%d", pParams->pTempBufferHeap);
	XESS_UNITY_LOG_FORMAT("pTempTextureHeap:%d", pParams->pTempTextureHeap);
}

void XeSSRenderAPI_D3D12::DebugPrintExecuteParams(xess_d3d12_execute_params_t* pParams)
{
	XESS_UNITY_LOG("XeSS Plugin: Executed! Param:");
	XESS_UNITY_LOG_FORMAT("descriptorHeapOffset:%d", pParams->descriptorHeapOffset);
	XESS_UNITY_LOG_FORMAT("exposureScale:%f", pParams->exposureScale);
	XESS_UNITY_LOG_FORMAT("inputColorBase:[%d, %d]", pParams->inputColorBase.x, pParams->inputColorBase.y);
	XESS_UNITY_LOG_FORMAT("inputDepthBase:[%d, %d]", pParams->inputDepthBase.x, pParams->inputDepthBase.y);
	XESS_UNITY_LOG_FORMAT("inputMotionVectorBase:[%d, %d]", pParams->inputMotionVectorBase.x, pParams->inputMotionVectorBase.y);
	XESS_UNITY_LOG_FORMAT("inputResponsiveMaskBase:[%d, %d]", pParams->inputResponsiveMaskBase.x, pParams->inputResponsiveMaskBase.y);
	XESS_UNITY_LOG_FORMAT("outputColorBase:[%d, %d]", pParams->outputColorBase.x, pParams->outputColorBase.y);
	XESS_UNITY_LOG_FORMAT("inputSize:[%d, %d]", pParams->inputWidth, pParams->inputHeight);
	XESS_UNITY_LOG_FORMAT("jitterOffset:[%f, %f]", pParams->jitterOffsetX, pParams->jitterOffsetY);
	XESS_UNITY_LOG_FORMAT("pColorTexture:[%d, %d]", (int)pParams->pColorTexture->GetDesc().Width, (int)pParams->pColorTexture->GetDesc().Height);
	XESS_UNITY_LOG_FORMAT("pDepthTexture:[%d, %d]", (int)pParams->pDepthTexture->GetDesc().Width, (int)pParams->pDepthTexture->GetDesc().Height);
	XESS_UNITY_LOG_FORMAT("pVelocityTexture:[%d, %d]", (int)pParams->pVelocityTexture->GetDesc().Width, (int)pParams->pVelocityTexture->GetDesc().Height);
	XESS_UNITY_LOG_FORMAT("pOutputTexture:[%d, %d]", (int)pParams->pOutputTexture->GetDesc().Width, (int)pParams->pOutputTexture->GetDesc().Height);
	XESS_UNITY_LOG("XeSS Plugin: Executed End!");
}

xess_result_t XeSSRenderAPI_D3D12::OnXeSSInitializeEvent(xess_context_handle_t hContext, void* pInitParams)
{
	if (!m_FP_xessD3D12Init)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	if (!ValidAndWaitCmdFinished(hContext))
		return XESS_RESULT_ERROR_UNKNOWN;

	{
		std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
		int contexIdx = FindContextStateIndex(hContext);
		if (contexIdx < 0)
			return XESS_RESULT_ERROR_UNKNOWN;

		const xess_d3d12_init_params_t* pParams = (const xess_d3d12_init_params_t*)pInitParams;
		return m_FP_xessD3D12Init(hContext, pParams);
	}
}

xess_result_t XeSSRenderAPI_D3D12::OnXeSSExecuteEvent(xess_context_handle_t hContext, void* pExecParams)
{
	if (!m_FP_xessD3D12Execute)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	xess_d3d12_execute_params_t* pParams = (xess_d3d12_execute_params_t*)pExecParams;
	xess_result_t ret = XESS_RESULT_ERROR_UNKNOWN;
	{
		std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
		int contexIdx = FindContextStateIndex(hContext);
		if (contexIdx < 0)
			return ret;

		ContextState_D3D12* context = (ContextState_D3D12*)m_ContextStates[contexIdx];

		if (!pParams->pColorTexture || !pParams->pDepthTexture || !pParams->pVelocityTexture || !pParams->pOutputTexture)
			return ret;

		m_D3D12->RequestResourceState(pParams->pColorTexture, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
		m_D3D12->RequestResourceState(pParams->pDepthTexture, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
		m_D3D12->RequestResourceState(pParams->pVelocityTexture, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
		m_D3D12->RequestResourceState(pParams->pOutputTexture, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
		if (pParams->pExposureScaleTexture)
		{
			m_D3D12->RequestResourceState(pParams->pExposureScaleTexture, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
		}
		if (pParams->pResponsivePixelMaskTexture)
		{
			m_D3D12->RequestResourceState(pParams->pResponsivePixelMaskTexture, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
		}

		UnityGraphicsD3D12RecordingState state;
		if (m_D3D12->CommandRecordingState(&state))
		{
			ID3D12GraphicsCommandList* commandList = state.commandList;
			ret = m_FP_xessD3D12Execute(hContext, commandList, pParams);
			if (ret == XESS_RESULT_SUCCESS)
			{
				context->SetFenceValue(m_D3D12->GetNextFrameFenceValue());
				context->IncrementInFlightIndex();
			}
		}
	}
	return ret;
}

bool XeSSRenderAPI_D3D12::ValidAndWaitCmdFinished(xess_context_handle_t hContext)
{
	// Should be careful about dead lock
	HANDLE waitEvent = NULL;
	{
		std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
		int contexIdx = FindContextStateIndex(hContext);
		if (contexIdx < 0)
			return false;

		ID3D12Fence* fence = m_D3D12->GetFrameFence();
		ContextState_D3D12* contextState = (ContextState_D3D12*)m_ContextStates[contexIdx];
		if (contextState && fence->GetCompletedValue() < contextState->GetFenceValue())
		{
			waitEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
			fence->SetEventOnCompletion(contextState->GetFenceValue(), waitEvent);
		}
	}
	if (waitEvent != NULL)
	{
		WaitForSingleObject(waitEvent, INFINITE);
		CloseHandle(waitEvent);
	}
	return true;
}

#endif // #if SUPPORT_D3D12
