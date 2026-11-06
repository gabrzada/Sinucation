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
#include "Unity/IUnityGraphics.h"
#include "XeSSLog.h"

extern IUnityLog* s_UnityLogPtr;

XeSSRenderAPI* CreateRenderAPI(UnityGfxRenderer apiType)
{
#if SUPPORT_D3D11
	if (apiType == kUnityGfxRendererD3D11)
	{
		extern XeSSRenderAPI* CreateRenderAPI_D3D11();
		return CreateRenderAPI_D3D11();
	}
#endif // if SUPPORT_D3D11

#if SUPPORT_D3D12
	if (apiType == kUnityGfxRendererD3D12)
	{
		extern XeSSRenderAPI* CreateRenderAPI_D3D12();
		return CreateRenderAPI_D3D12();
	}
#endif // if SUPPORT_D3D12

#if SUPPORT_VULKAN
	if (apiType == kUnityGfxRendererVulkan)
	{
		extern XeSSRenderAPI* CreateRenderAPI_Vulkan();
		return CreateRenderAPI_Vulkan();
	}
#endif	// if SUPPORT_VULKAN

	// Unknown or unsupported graphics API
	return NULL;
}

bool XeSSRenderAPI::LoadXeSSLibrary(bool isD3D11)
{
	wchar_t path[MAX_PATH];
	HMODULE hm = NULL;
	if (GetModuleHandleEx(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT, (LPCSTR)&UnityPluginLoad, &hm) == 0)
	{
		int ret = GetLastError();
		XESS_UNITY_LOG_FORMAT("GetModuleHandle failed, error: 0x%x", ret);
		return false;
	}
	if (GetModuleFileNameW(hm, path, MAX_PATH) == 0)
	{
		int ret = GetLastError();
		XESS_UNITY_LOG_FORMAT("GetModuleFileName failed, error: 0x%x", ret);
		return false;
	}

	wchar_t dlldir[MAX_PATH];
	if (_wsplitpath_s(path, NULL, 0, dlldir, MAX_PATH, NULL, 0, NULL, 0) == 0)
	{
		const wchar_t* dllname = isD3D11 ? L"libxess_dx11.dll" : L"libxess.dll";
		if (SUCCEEDED(StringCchCatW(dlldir, MAX_PATH, dllname)))
		{
			m_XeSSLib = ::LoadLibraryExW(dlldir, NULL, NULL);
			if (m_XeSSLib == NULL)
			{
				XESS_UNITY_LOG_FORMAT("Can't load library %s, dllpath: %s, error: 0x%x", dllname, dlldir, GetLastError());
				return false;
			}

			// load functions
			m_FP_xessDestroyContext = (FP_xessDestroyContext)GetProcAddress(m_XeSSLib, "xessDestroyContext");
			return true;
		}
	}

	return false;
}

bool XeSSRenderAPI::XeSSIsValid(xess_context_handle_t hContext)
{
	std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
	return (FindContextStateIndex(hContext) != -1);
}

xess_result_t XeSSRenderAPI::OnXeSSDestroyContextEvent(xess_context_handle_t hContext)
{
	if (!m_FP_xessDestroyContext)
		return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

	if (!ValidAndWaitCmdFinished(hContext))
		return XESS_RESULT_ERROR_UNKNOWN;

	{
		std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
		int idx = FindContextStateIndex(hContext);
		if (idx >= 0)
		{
			delete m_ContextStates[idx];
			m_ContextStates.erase(m_ContextStates.begin() + idx);
			return m_FP_xessDestroyContext(hContext);
		}
	}

	return XESS_RESULT_ERROR_UNKNOWN;
}

void XeSSRenderAPI::ReleaseContextsResource()
{
	std::lock_guard<std::mutex> lock(m_ContextStatesMutex);

	if (m_ContextStates.size() > 0)
	{
		XESS_UNITY_LOG_FORMAT("left undestroied contexts, count: %d, start destroy", m_ContextStates.size());

		for (int i = 0; i < m_ContextStates.size(); i++)
		{
			if (m_ContextStates[i])
			{
				if(m_FP_xessDestroyContext)
				{
					m_FP_xessDestroyContext(m_ContextStates[i]->GetContext());
				}
				delete m_ContextStates[i];
			}
		}

		XESS_UNITY_LOG_FORMAT("left undestroied contexts destroy finished");
	}
}

int XeSSRenderAPI::FindContextStateIndex(xess_context_handle_t hContext)
{
	for (int i = 0; i < m_ContextStates.size(); i++)
	{
		if (m_ContextStates[i] && m_ContextStates[i]->GetContext() == hContext)
			return i;
	}
	return -1;
}