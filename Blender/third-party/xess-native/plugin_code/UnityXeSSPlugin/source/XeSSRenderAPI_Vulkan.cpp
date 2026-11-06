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

// Vulkan implementation of RenderAPI.
#if SUPPORT_VULKAN
#include <iostream>
#include <cmath>
#include <stdio.h>
#include <map>

#define VK_NO_PROTOTYPES
#include "Unity/IUnityGraphicsVulkan.h"

#include "XeSSLog.h"
#include "XeSSSDK/inc/xess/xess_vk.h"
#include "XeSSSDK/inc/xess/xess_vk_debug.h"

#define UNITY_USED_VULKAN_API_FUNCTIONS(apply) \
    apply(vkCreateInstance); \
    apply(vkCreateDevice); \
    apply(vkCmdBeginRenderPass); \
    apply(vkCreateBuffer); \
    apply(vkGetPhysicalDeviceMemoryProperties); \
    apply(vkGetBufferMemoryRequirements); \
    apply(vkCreateImageView);   \
    apply(vkDestroyImageView);  \
    apply(vkCreateFence);   \
    apply(vkDestroyFence);  \
    apply(vkResetFences);   \
    apply(vkWaitForFences); \
    apply(vkCreateCommandPool); \
    apply(vkDestroyCommandPool);    \
    apply(vkAllocateCommandBuffers);    \
    apply(vkFreeCommandBuffers);    \
    apply(vkBeginCommandBuffer);    \
    apply(vkEndCommandBuffer);  \
    apply(vkQueueSubmit);   \
    apply(vkCmdPipelineBarrier);    \
    apply(vkMapMemory); \
    apply(vkBindBufferMemory); \
    apply(vkAllocateMemory); \
    apply(vkDestroyBuffer); \
    apply(vkFreeMemory); \
    apply(vkUnmapMemory); \
    apply(vkQueueWaitIdle); \
    apply(vkDeviceWaitIdle); \
    apply(vkCmdCopyBufferToImage); \
    apply(vkFlushMappedMemoryRanges); \
    apply(vkCreatePipelineLayout); \
    apply(vkCreateShaderModule); \
    apply(vkDestroyShaderModule); \
    apply(vkCreateGraphicsPipelines); \
    apply(vkCmdBindPipeline); \
    apply(vkCmdDraw); \
    apply(vkCmdPushConstants); \
    apply(vkCmdBindVertexBuffers); \
    apply(vkDestroyPipeline); \
    apply(vkDestroyPipelineLayout);

#define VULKAN_DEFINE_API_FUNCPTR(func) static PFN_##func func
VULKAN_DEFINE_API_FUNCPTR(vkGetInstanceProcAddr);
UNITY_USED_VULKAN_API_FUNCTIONS(VULKAN_DEFINE_API_FUNCPTR);
#undef VULKAN_DEFINE_API_FUNCPTR

#define ENABLE_DEBUG_LAYER (0)

static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API InterceptVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*);

extern "C" void RenderAPI_Vulkan_OnPluginLoad(IUnityInterfaces * interfaces)
{
    if (IUnityGraphicsVulkanV2* vulkanInterface = interfaces->Get<IUnityGraphicsVulkanV2>())
        vulkanInterface->AddInterceptInitialization(InterceptVulkanInitialization, NULL, 0);
    else if (IUnityGraphicsVulkan* vulkanInterface = interfaces->Get<IUnityGraphicsVulkan>())
        vulkanInterface->InterceptInitialization(InterceptVulkanInitialization, NULL);
}

static VkInstance s_instance = NULL;
static bool s_xessPreCreationFailed = false;

static void LoadVulkanAPI(PFN_vkGetInstanceProcAddr getInstanceProcAddr, VkInstance instance)
{
    if (!vkGetInstanceProcAddr && getInstanceProcAddr)
        vkGetInstanceProcAddr = getInstanceProcAddr;

    if (!vkCreateInstance)
        vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkCreateInstance");

#define LOAD_VULKAN_FUNC(fn) if (!fn) fn = (PFN_##fn)vkGetInstanceProcAddr(instance, #fn)
    UNITY_USED_VULKAN_API_FUNCTIONS(LOAD_VULKAN_FUNC);
#undef LOAD_VULKAN_FUNC
}

static VKAPI_ATTR VkResult VKAPI_CALL Hook_vkCreateInstance(const VkInstanceCreateInfo* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkInstance* pInstance)
{
    uint32_t xessExtensionCount = 0;
    const char* const* xessExtensions;
    uint32_t minVKApiVersion;
    std::vector<const char*> instanceExtensions;
#if ENABLE_DEBUG_LAYER
    std::vector<const char*> instanceLayers;
#endif
    VkInstanceCreateInfo* pXeSSCreateInfo = (VkInstanceCreateInfo*)pCreateInfo;
    VkApplicationInfo xessAppInfo;
    xess_result_t ret;
    if ((ret = xessVKGetRequiredInstanceExtensions(&xessExtensionCount, &xessExtensions, &minVKApiVersion)) == XESS_RESULT_SUCCESS)
    {
        if (pCreateInfo->pApplicationInfo->apiVersion < minVKApiVersion)
        {
            memcpy(&xessAppInfo, pCreateInfo->pApplicationInfo, sizeof(VkApplicationInfo));
            xessAppInfo.apiVersion = minVKApiVersion;
            pXeSSCreateInfo->pApplicationInfo = &xessAppInfo;
        }
        for (uint32_t i = 0; i < pCreateInfo->enabledExtensionCount; i++)
        {
            instanceExtensions.push_back(pCreateInfo->ppEnabledExtensionNames[i]);
        }
        for (uint32_t i = 0; i < xessExtensionCount; i++)
        {
            instanceExtensions.push_back(xessExtensions[i]);
            XESS_UNITY_LOG_FORMAT("[xessVKGetRequiredInstanceExtensions] extensions: name=%s", xessExtensions[i]);
        }

#if ENABLE_DEBUG_LAYER
        instanceExtensions.push_back(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
#endif

        pXeSSCreateInfo->enabledExtensionCount = (uint32_t)instanceExtensions.size();
        pXeSSCreateInfo->ppEnabledExtensionNames = instanceExtensions.data();

#if ENABLE_DEBUG_LAYER
        for (uint32_t i = 0; i < pCreateInfo->enabledLayerCount; i++)
        {
            instanceLayers.push_back(pCreateInfo->ppEnabledLayerNames[i]);
        }
        instanceLayers.push_back("VK_LAYER_KHRONOS_validation");
        pXeSSCreateInfo->enabledLayerCount = (uint32_t)instanceLayers.size();
        pXeSSCreateInfo->ppEnabledLayerNames = instanceLayers.data();
#endif
    }
    else
    {
        s_xessPreCreationFailed = true;
        XESS_UNITY_LOG_FORMAT("XeSS VK pre-creation failed: xessVKGetRequiredInstanceExtensions failed. [ret(%d)]", ret);
    }

    vkCreateInstance = (PFN_vkCreateInstance)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkCreateInstance");
    VkResult result = vkCreateInstance(pCreateInfo, pAllocator, pInstance);
    if (result == VK_SUCCESS)
    {
        XESS_UNITY_LOG("vkCreateInstance in hook success.");
        s_instance = *pInstance;
        LoadVulkanAPI(vkGetInstanceProcAddr, *pInstance);
    }

    return result;
}

static VKAPI_ATTR VkResult VKAPI_CALL Hook_vkCreateDevice(VkPhysicalDevice physicalDevice, const VkDeviceCreateInfo* pCreateInfo, const VkAllocationCallbacks* pAllocator, VkDevice* pDevice)
{
    uint32_t xessExtensionCount = 0;
    const char* const* xessExtensions;
    std::vector<const char*> instanceExtensions;
    VkDeviceCreateInfo* pXeSSCreateInfo = (VkDeviceCreateInfo*)pCreateInfo;
    xess_result_t ret;
    if ((ret = xessVKGetRequiredDeviceExtensions(s_instance, physicalDevice, &xessExtensionCount, &xessExtensions)) == XESS_RESULT_SUCCESS)
    {
        for (uint32_t i = 0; i < pCreateInfo->enabledExtensionCount; i++)
        {
            instanceExtensions.push_back(pCreateInfo->ppEnabledExtensionNames[i]);
        }
        for (uint32_t i = 0; i < xessExtensionCount; i++)
        {
            instanceExtensions.push_back(xessExtensions[i]);
            XESS_UNITY_LOG_FORMAT("[xessVKGetRequiredDeviceExtensions] extensions: name=%s", xessExtensions[i]);
        }

        pXeSSCreateInfo->enabledExtensionCount = (uint32_t)instanceExtensions.size();
        pXeSSCreateInfo->ppEnabledExtensionNames = instanceExtensions.data();
    }
    else
    {
        s_xessPreCreationFailed = true;
        XESS_UNITY_LOG_FORMAT("XeSS VK pre-creation failed: xessVKGetRequiredDeviceExtensions failed. [ret(%d)]", ret);
    }

    void* features = (void*)pXeSSCreateInfo->pNext;
    if ((ret = xessVKGetRequiredDeviceFeatures(s_instance, physicalDevice, &features)) == XESS_RESULT_SUCCESS)
    {
        pXeSSCreateInfo->pNext = features;
    }
    else
    {
        s_xessPreCreationFailed = true;
        XESS_UNITY_LOG_FORMAT("XeSS VK pre-creation failed: xessVKGetRequiredDeviceFeatures failed. [ret(%d)]", ret);
    }

    vkCreateDevice = (PFN_vkCreateDevice)vkGetInstanceProcAddr(s_instance, "vkCreateDevice");
    return vkCreateDevice(physicalDevice, pXeSSCreateInfo, pAllocator, pDevice);
}

static VKAPI_ATTR PFN_vkVoidFunction VKAPI_CALL Hook_vkGetInstanceProcAddr(VkInstance device, const char* funcName)
{
    if (!funcName)
        return NULL;

#define INTERCEPT(fn) if (strcmp(funcName, #fn) == 0) return (PFN_vkVoidFunction)&Hook_##fn
    INTERCEPT(vkCreateInstance);
    INTERCEPT(vkCreateDevice);
#undef INTERCEPT

    return vkGetInstanceProcAddr(device, funcName);;
}

static PFN_vkGetInstanceProcAddr UNITY_INTERFACE_API InterceptVulkanInitialization(PFN_vkGetInstanceProcAddr getInstanceProcAddr, void*)
{
    vkGetInstanceProcAddr = getInstanceProcAddr;
    return Hook_vkGetInstanceProcAddr;
}

extern IUnityLog* s_UnityLogPtr;

XESS_PACK_B()
typedef struct _xess_vk_native_execute_params_t
{
    /** Input color texture. Must be in VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL state.*/
    VkImage* colorTexture;
    /** Input motion vector texture. Must be in VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL state.*/
    VkImage* velocityTexture;
    /** Optional depth texture. Required if XESS_INIT_FLAG_HIGH_RES_MV has not been specified.
     * Must be in VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL state.*/
    VkImage* depthTexture;
    /** Optional 1x1 exposure scale texture. Required if XESS_INIT_FLAG_EXPOSURE_TEXTURE has been
     * specified. Must be in VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL state */
    VkImage* exposureScaleTexture;
    /** Optional responsive pixel mask texture. Required if XESS_INIT_FLAG_RESPONSIVE_PIXEL_MASK
     * has been specified. Must be in VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL state */
    VkImage* responsivePixelMaskTexture;
    /** Output texture in target resolution. Must be in VK_IMAGE_LAYOUT_GENERAL state.*/
    VkImage* outputTexture;

    /** Jitter X coordinate in the range [-0.5, 0.5]. */
    float jitterOffsetX;
    /** Jitter Y coordinate in the range [-0.5, 0.5]. */
    float jitterOffsetY;
    /** Optional input color scaling. Default is 1. */
    float exposureScale;
    /** Resets the history accumulation in this frame. */
    uint32_t resetHistory;
    /** Input color width. */
    uint32_t inputWidth;
    /** Input color height. */
    uint32_t inputHeight;
    /** Base coordinate for the input color in the texture. Default is (0,0). */
    xess_coord_t inputColorBase;
    /** Base coordinate for the input motion vector in the texture.  Default is (0,0). */
    xess_coord_t inputMotionVectorBase;
    /** Base coordinate for the input depth in the texture. Default is (0,0). */
    xess_coord_t inputDepthBase;
    /** Base coordinate for the input responsive pixel mask in the texture. Default is (0,0). */
    xess_coord_t inputResponsiveMaskBase;
    /** Reserved parameter. */
    xess_coord_t reserved0;
    /** Base coordinate for the output color.  Default is (0,0). */
    xess_coord_t outputColorBase;
} xess_vk_native_execute_params_t;
XESS_PACK_E()

typedef xess_result_t(*FP_xessVKCreateContext)(VkInstance instance, VkPhysicalDevice physicalDevice, VkDevice device, xess_context_handle_t* phContext);
typedef xess_result_t(*FP_xessVKBuildPipelines)(xess_context_handle_t hContext, VkPipelineCache pipelineCache, bool blocking, uint32_t initFlags);
typedef xess_result_t(*FP_xessVKGetInitParams)(xess_context_handle_t hContext, xess_vk_init_params_t* pInitParams);
typedef xess_result_t(*FP_xessVKInit)(xess_context_handle_t hContext, const xess_vk_init_params_t* pInitParams);
typedef xess_result_t(*FP_xessVKExecute)(xess_context_handle_t hContext, VkCommandBuffer commandBuffer, const xess_vk_execute_params_t* pExecParams);

class ContextState_VK : public ContextState
{
public:
    ContextState_VK(xess_context_handle_t context) : ContextState(context)
    {
        m_CallInitialized = false;

        VkFenceCreateInfo fenceInfo = {};
        fenceInfo.sType = VK_STRUCTURE_TYPE_FENCE_CREATE_INFO;
        fenceInfo.flags = VK_FENCE_CREATE_SIGNALED_BIT;
        if (vkCreateFence(m_pInstance->device, &fenceInfo, nullptr, &m_Fence) != VK_SUCCESS)
        {
            XESS_UNITY_LOG("create fence failed.");
        }
        VkCommandBufferAllocateInfo allocInfo = {};
        allocInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO;
        allocInfo.commandPool = *m_pCommandPool;
        allocInfo.level = VK_COMMAND_BUFFER_LEVEL_PRIMARY;
        allocInfo.commandBufferCount = MAX_IN_FLIGHT;
        if (vkAllocateCommandBuffers(m_pInstance->device, &allocInfo, m_CommandBuffer) != VK_SUCCESS)
        {
            XESS_UNITY_LOG("create command buffer failed.");
        }
    }
    virtual ~ContextState_VK() 
    {
        if (m_Fence)
        {
            vkDestroyFence(m_pInstance->device, m_Fence, nullptr);
        }
        if (m_pCommandPool && m_CommandBuffer[0])
        {
            vkFreeCommandBuffers(m_pInstance->device, *m_pCommandPool, MAX_IN_FLIGHT, m_CommandBuffer);
        }
        for (auto iter = m_ImageViews.begin(); iter != m_ImageViews.end(); iter++)
        {
            if (iter->second.imageView)
            {
                vkDestroyImageView(m_pInstance->device, iter->second.imageView, nullptr);
            }
        }
        m_ImageViews.clear();
    }

    std::map<const char*, xess_vk_image_view_info>& GetImageViews() { return m_ImageViews; }

    static void SetUnityVulkanInstance(UnityVulkanInstance* instance) { m_pInstance = instance; }
    static void SetVkCommandPool(VkCommandPool* commandPool) { m_pCommandPool = commandPool; }

    VkFence& GetFence() { return m_Fence; }

    VkCommandBuffer& GetCommandBuffer() { return m_CommandBuffer[m_InflightIndex]; }

    // patch the layout, not know why ...
    bool m_CallInitialized;

protected:
    std::map<const char*, xess_vk_image_view_info> m_ImageViews;
    VkFence m_Fence;
    VkCommandBuffer m_CommandBuffer[MAX_IN_FLIGHT];

    static UnityVulkanInstance* m_pInstance;
    static VkCommandPool* m_pCommandPool;
};
UnityVulkanInstance* ContextState_VK::m_pInstance = NULL;
VkCommandPool* ContextState_VK::m_pCommandPool = NULL;

class XeSSRenderAPI_Vulkan : public XeSSRenderAPI
{
public:
    XeSSRenderAPI_Vulkan();
    virtual ~XeSSRenderAPI_Vulkan() { }

    virtual void ProcessDeviceEvent(UnityGfxDeviceEventType type, IUnityInterfaces* interfaces);

    // XeSS
    virtual xess_result_t XeSSCreateContext(xess_context_handle_t* phContext);
    virtual xess_result_t XeSSBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags);
    virtual xess_result_t XeSSGetInitParams(xess_context_handle_t hContext, void* pInitParams);

    // XeSS rendering events
    virtual xess_result_t OnXeSSInitializeEvent(xess_context_handle_t hContext, void* pInitParams);
    virtual xess_result_t OnXeSSExecuteEvent(xess_context_handle_t hContext, void* pExecParams);

private:
    virtual bool ValidAndWaitCmdFinished(xess_context_handle_t hContext);

    VkImageView CreateImageView(VkImage image, VkFormat format, VkImageSubresourceRange subRes);

    bool AccessTexture(UnityVulkanImage& unityVulkanImage, const char* name, ContextState_VK* context, VkImage* tex, xess_vk_image_view_info* info, bool canSkip = false, bool write = false);
    void TranstionTextureLayout(VkCommandBuffer commandBuffer, const UnityVulkanImage& unityVulkanImage, VkImageLayout oldLayout, VkImageLayout newLayout, VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage, VkAccessFlags srcAccess, VkAccessFlags dstAccess);
    VkPipelineStageFlags GetStageFlagsFromLayout(VkImageLayout layout);
    VkAccessFlags GetAccessFlagsFromLayout(VkImageLayout layout);

    void ConfigPlugin(IUnityGraphicsVulkan* vulkan);
    void CreateResources();
    void ReleaseResources();

private:
    VkCommandPool m_CommandPool;

    IUnityGraphicsVulkan* m_Vulkan;
    UnityVulkanInstance m_Instance;

    // XeSS functions
    FP_xessVKCreateContext m_FP_xessVKCreateContext;
    FP_xessVKBuildPipelines m_FP_xessVKBuildPipelines;
    FP_xessVKGetInitParams m_FP_xessVKGetInitParams;
    FP_xessVKInit m_FP_xessVKInit;
    FP_xessVKExecute m_FP_xessVKExecute;
};

XeSSRenderAPI* CreateRenderAPI_Vulkan()
{
    return new XeSSRenderAPI_Vulkan();
}

XeSSRenderAPI_Vulkan::XeSSRenderAPI_Vulkan()
    : m_Vulkan(NULL)
    , m_FP_xessVKCreateContext(NULL)
	, m_FP_xessVKBuildPipelines(NULL)
	, m_FP_xessVKGetInitParams(NULL)
	, m_FP_xessVKInit(NULL)
	, m_FP_xessVKExecute(NULL)
{
    if (LoadXeSSLibrary(false))
    {
        // load functions
        m_FP_xessVKCreateContext = (FP_xessVKCreateContext)GetProcAddress(m_XeSSLib, "xessVKCreateContext");
        m_FP_xessVKBuildPipelines = (FP_xessVKBuildPipelines)GetProcAddress(m_XeSSLib, "xessVKBuildPipelines");
        m_FP_xessVKGetInitParams = (FP_xessVKGetInitParams)GetProcAddress(m_XeSSLib, "xessVKGetInitParams");
        m_FP_xessVKInit = (FP_xessVKInit)GetProcAddress(m_XeSSLib, "xessVKInit");
        m_FP_xessVKExecute = (FP_xessVKExecute)GetProcAddress(m_XeSSLib, "xessVKExecute");
    }
}

void XeSSRenderAPI_Vulkan::ProcessDeviceEvent(UnityGfxDeviceEventType type, IUnityInterfaces* interfaces)
{
    switch (type)
    {
    case kUnityGfxDeviceEventInitialize:
    case kUnityGfxDeviceEventAfterReset:
        m_Vulkan = interfaces->Get<IUnityGraphicsVulkan>();
        if (m_Vulkan == NULL)
        {
            XESS_UNITY_LOG("IUnityGraphicsVulkan device get failed!");
            break;
        }
        m_Instance = m_Vulkan->Instance();

        // Make sure Vulkan API functions are loaded
        LoadVulkanAPI(m_Instance.getInstanceProcAddr, m_Instance.instance);

        ConfigPlugin(m_Vulkan);
        CreateResources();
        break;
    case kUnityGfxDeviceEventShutdown:
    case kUnityGfxDeviceEventBeforeReset:
        if (m_Vulkan)
        {
            ReleaseResources();
            m_Vulkan = NULL;
            m_Instance = UnityVulkanInstance();
        }
        break;
    }
}

// XeSS
xess_result_t XeSSRenderAPI_Vulkan::XeSSCreateContext(xess_context_handle_t* phContext)
{
    if (!m_FP_xessVKCreateContext)
        return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

    if (s_xessPreCreationFailed)
        return XESS_RESULT_ERROR_UNSUPPORTED;

    xess_result_t ret = m_FP_xessVKCreateContext(m_Instance.instance, m_Instance.physicalDevice, m_Instance.device, phContext);
    if (ret == XESS_RESULT_SUCCESS)
    {
        ContextState_VK* cs = new ContextState_VK(*phContext);
        m_ContextStates.push_back(cs);
    }
    return ret;
}

xess_result_t XeSSRenderAPI_Vulkan::XeSSBuildPipelines(xess_context_handle_t hContext, bool blocking, uint32_t initFlags)
{
    if (!m_FP_xessVKBuildPipelines)
        return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;
    return m_FP_xessVKBuildPipelines(hContext, m_Instance.pipelineCache, blocking, initFlags);
}

xess_result_t XeSSRenderAPI_Vulkan::XeSSGetInitParams(xess_context_handle_t hContext, void* pInitParams)
{
    if (!m_FP_xessVKGetInitParams)
        return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;
    return m_FP_xessVKGetInitParams(hContext, (xess_vk_init_params_t*)pInitParams);
}

xess_result_t XeSSRenderAPI_Vulkan::OnXeSSInitializeEvent(xess_context_handle_t hContext, void* pInitParams)
{
    if (!m_FP_xessVKInit)
        return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

    if (!ValidAndWaitCmdFinished(hContext))
        return XESS_RESULT_ERROR_UNKNOWN;
    
    {
        std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
        int contexIdx = FindContextStateIndex(hContext);
        if (contexIdx < 0)
            return XESS_RESULT_ERROR_UNKNOWN;

        const xess_vk_init_params_t* pParams = (const xess_vk_init_params_t*)pInitParams;
        xess_result_t ret = m_FP_xessVKInit(hContext, pParams);
        if (ret == XESS_RESULT_SUCCESS)
        {
            ContextState_VK* context = (ContextState_VK*)m_ContextStates[contexIdx];
            context->m_CallInitialized = true;
        }
        return ret;
    }
}

VkImageView XeSSRenderAPI_Vulkan::CreateImageView(VkImage image, VkFormat format, VkImageSubresourceRange subRes)
{
    VkImageViewCreateInfo viewInfo = {};
    viewInfo.sType = VK_STRUCTURE_TYPE_IMAGE_VIEW_CREATE_INFO;
    viewInfo.image = image;
    viewInfo.viewType = VK_IMAGE_VIEW_TYPE_2D;
    viewInfo.format = format;
    viewInfo.subresourceRange = subRes;

    VkImageView imageView;
    if (vkCreateImageView(m_Instance.device, &viewInfo, nullptr, &imageView) != VK_SUCCESS) {
        XESS_UNITY_LOG("VkImageView creation for XeSS inputs failed.");
        return NULL;
    }

    return imageView;
}

bool XeSSRenderAPI_Vulkan::AccessTexture(UnityVulkanImage& unityVulkanImage, const char* name, ContextState_VK* context, VkImage* tex, xess_vk_image_view_info* info, bool canskip, bool write)
{
    unityVulkanImage.image = VK_NULL_HANDLE;

    if (tex == NULL)
        return canskip;

    if (!m_Vulkan->AccessTexture(tex, UnityVulkanWholeImage, write ? VK_IMAGE_LAYOUT_GENERAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, 0, write ? VK_ACCESS_SHADER_WRITE_BIT : VK_ACCESS_SHADER_READ_BIT, kUnityVulkanResourceAccess_ObserveOnly, &unityVulkanImage))
    {
        XESS_UNITY_LOG_FORMAT("Failed to access texture due to AccessTexture: %s", name);
        return false;
    }

    info->image = *tex;
    info->format = unityVulkanImage.format;
    info->width = unityVulkanImage.extent.width;
    info->height = unityVulkanImage.extent.height;
    info->subresourceRange.aspectMask = unityVulkanImage.aspect;
    if (strcmp(name, "depth") == 0)
    {
        info->subresourceRange.aspectMask &= ~VK_IMAGE_ASPECT_STENCIL_BIT;
    }
    info->subresourceRange.baseArrayLayer = 0;
    info->subresourceRange.layerCount = unityVulkanImage.layers;
    info->subresourceRange.baseMipLevel = 0;
    info->subresourceRange.levelCount = unityVulkanImage.mipCount;

    std::map<const char*, xess_vk_image_view_info>& imageViews = context->GetImageViews();
    std::map<const char*, xess_vk_image_view_info>::iterator iter = imageViews.find(name);
    if (iter != imageViews.end())
    {
        if (info->image != iter->second.image)
        {
            if (iter->second.imageView)
            {
                vkDestroyImageView(m_Instance.device, iter->second.imageView, nullptr);
            }
            info->imageView = CreateImageView(info->image, info->format, info->subresourceRange);
            imageViews[name] = *info;
        }
        else
        {
            info->imageView = iter->second.imageView;
        }
    }
    else
    {
        info->imageView = CreateImageView(info->image, info->format, info->subresourceRange);
        imageViews.insert(std::pair<const char*, xess_vk_image_view_info>(name, *info));
    }

    return true;
}

void XeSSRenderAPI_Vulkan::TranstionTextureLayout(VkCommandBuffer commandBuffer, const UnityVulkanImage& unityVulkanImage, VkImageLayout oldLayout, VkImageLayout newLayout, VkPipelineStageFlags srcStage, VkPipelineStageFlags dstStage, VkAccessFlags srcAccess, VkAccessFlags dstAccess)
{
    if (unityVulkanImage.image == VK_NULL_HANDLE || oldLayout == newLayout)
    {
        return;
    }

    // transition layout
    VkImageMemoryBarrier barrier = {};
    barrier.sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER;
    barrier.oldLayout = oldLayout;
    barrier.newLayout = newLayout;
    barrier.srcQueueFamilyIndex = m_Instance.queueFamilyIndex;
    barrier.dstQueueFamilyIndex = m_Instance.queueFamilyIndex;
    barrier.image = unityVulkanImage.image;
    barrier.subresourceRange.aspectMask = unityVulkanImage.aspect;
    barrier.subresourceRange.baseArrayLayer = 0;
    barrier.subresourceRange.layerCount = unityVulkanImage.layers;
    barrier.subresourceRange.baseMipLevel = 0;
    barrier.subresourceRange.levelCount = unityVulkanImage.mipCount;
    barrier.srcAccessMask = (srcAccess == 0) ? GetAccessFlagsFromLayout(oldLayout) : srcAccess;
    barrier.dstAccessMask = (dstAccess == 0) ? GetAccessFlagsFromLayout(newLayout) : dstAccess;
    srcStage = (srcStage == 0) ? GetStageFlagsFromLayout(oldLayout) : srcStage;
    dstStage = (dstStage == 0) ? GetStageFlagsFromLayout(newLayout) : dstStage;

    vkCmdPipelineBarrier(
        commandBuffer,
        srcStage, dstStage,
        0,
        0, nullptr,
        0, nullptr,
        1, &barrier
    );
}

VkPipelineStageFlags XeSSRenderAPI_Vulkan::GetStageFlagsFromLayout(VkImageLayout layout)
{
    switch (layout)
    {
    case VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL:
        return VK_PIPELINE_STAGE_COLOR_ATTACHMENT_OUTPUT_BIT;
        break;
    case VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL:
        return VK_PIPELINE_STAGE_EARLY_FRAGMENT_TESTS_BIT;
        break;
    case VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:
        return VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
        break;
    case VK_IMAGE_LAYOUT_GENERAL:
        return VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT;
        break;
    case VK_IMAGE_LAYOUT_UNDEFINED:
        return VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
        break;
    }
    return VK_PIPELINE_STAGE_TOP_OF_PIPE_BIT;
}

VkAccessFlags XeSSRenderAPI_Vulkan::GetAccessFlagsFromLayout(VkImageLayout layout)
{
    switch (layout)
    {
    case VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL:
        return VK_ACCESS_COLOR_ATTACHMENT_READ_BIT | VK_ACCESS_COLOR_ATTACHMENT_WRITE_BIT;
        break;
    case VK_IMAGE_LAYOUT_DEPTH_STENCIL_ATTACHMENT_OPTIMAL:
        return VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_READ_BIT | VK_ACCESS_DEPTH_STENCIL_ATTACHMENT_WRITE_BIT;
        break;
    case VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL:
        return VK_ACCESS_SHADER_READ_BIT;
        break;
    case VK_IMAGE_LAYOUT_GENERAL:
        return VK_ACCESS_SHADER_WRITE_BIT;
        break;
    case VK_IMAGE_LAYOUT_UNDEFINED:
        return 0;
        break;
    }
    return 0;
}

xess_result_t XeSSRenderAPI_Vulkan::OnXeSSExecuteEvent(xess_context_handle_t hContext, void* pExecParams)
{
    if (!m_FP_xessVKExecute)
        return XESS_RESULT_ERROR_CANT_LOAD_LIBRARY;

    xess_vk_native_execute_params_t* pParams = (xess_vk_native_execute_params_t*)pExecParams;

    xess_vk_execute_params_t VkParam = {};
    VkParam.jitterOffsetX = pParams->jitterOffsetX;
    VkParam.jitterOffsetY = pParams->jitterOffsetY;
    VkParam.exposureScale = pParams->exposureScale;
    VkParam.resetHistory = pParams->resetHistory;
    VkParam.inputWidth = pParams->inputWidth;
    VkParam.inputHeight = pParams->inputHeight;
    VkParam.inputColorBase = pParams->inputColorBase;
    VkParam.inputMotionVectorBase = pParams->inputMotionVectorBase;
    VkParam.inputDepthBase = pParams->inputDepthBase;
    VkParam.inputResponsiveMaskBase = pParams->inputResponsiveMaskBase;
    VkParam.reserved0 = pParams->reserved0;
    VkParam.outputColorBase = pParams->outputColorBase;

    {
        std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
        int contexIdx = FindContextStateIndex(hContext);
        if (contexIdx < 0)
            return XESS_RESULT_ERROR_UNKNOWN;

        ContextState_VK* context = (ContextState_VK*)m_ContextStates[contexIdx];
        UnityVulkanImage images[6];
        if (!AccessTexture(images[0], "color", context, pParams->colorTexture, &VkParam.colorTexture) ||
            !AccessTexture(images[1], "velocity", context, pParams->velocityTexture, &VkParam.velocityTexture) ||
            !AccessTexture(images[2], "depth", context, pParams->depthTexture, &VkParam.depthTexture) ||
            !AccessTexture(images[3], "exposure", context, pParams->exposureScaleTexture, &VkParam.exposureScaleTexture, true) ||
            !AccessTexture(images[4], "responsive", context, pParams->responsivePixelMaskTexture, &VkParam.responsivePixelMaskTexture, true) ||
            !AccessTexture(images[5], "output", context, pParams->outputTexture, &VkParam.outputTexture, false, true))
        {
            XESS_UNITY_LOG("Failed to access texture.");
            return XESS_RESULT_ERROR_UNKNOWN;
        }

        VkCommandBuffer commandBuffer = context->GetCommandBuffer();
        if (!commandBuffer)
            return XESS_RESULT_ERROR_UNKNOWN;

        VkCommandBufferBeginInfo beginInfo = {};
        beginInfo.sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO;

        vkBeginCommandBuffer(commandBuffer, &beginInfo);

        /*for (int i = 0; i < 6; i++)
        {
            if (images[i].image)
            {
                XESS_UNITY_LOG_FORMAT("command buffer: 0x%x, image[%d] layout: %d, image: 0x%x", m_CommandBuffer, i, images[i].layout, images[i].image);
            }
        }*/

        // set layout for xess execution
        TranstionTextureLayout(commandBuffer, images[0], VK_IMAGE_LAYOUT_UNDEFINED, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT);
        TranstionTextureLayout(commandBuffer, images[1], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT);
        TranstionTextureLayout(commandBuffer, images[2], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT);
        TranstionTextureLayout(commandBuffer, images[3], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT);
        TranstionTextureLayout(commandBuffer, images[4], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT);
        TranstionTextureLayout(commandBuffer, images[5], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_GENERAL, 0, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_WRITE_BIT);

        xess_result_t ret = m_FP_xessVKExecute(hContext, commandBuffer, &VkParam);

        // restore layout
        TranstionTextureLayout(commandBuffer, images[0], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, context->m_CallInitialized ? VK_IMAGE_LAYOUT_COLOR_ATTACHMENT_OPTIMAL : VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT, 0);
        TranstionTextureLayout(commandBuffer, images[1], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT, 0);
        TranstionTextureLayout(commandBuffer, images[2], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT, 0);
        TranstionTextureLayout(commandBuffer, images[3], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT, 0);
        TranstionTextureLayout(commandBuffer, images[4], VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, 0, VK_ACCESS_SHADER_READ_BIT, 0);
        TranstionTextureLayout(commandBuffer, images[5], VK_IMAGE_LAYOUT_GENERAL, VK_IMAGE_LAYOUT_SHADER_READ_ONLY_OPTIMAL, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_ACCESS_SHADER_WRITE_BIT, VK_ACCESS_SHADER_READ_BIT);

        vkEndCommandBuffer(commandBuffer);
        context->m_CallInitialized = false;
        
        if (ret == XESS_RESULT_SUCCESS)
        {
            VkSubmitInfo submitInfo = {};
            submitInfo.sType = VK_STRUCTURE_TYPE_SUBMIT_INFO;
            submitInfo.commandBufferCount = 1;
            submitInfo.pCommandBuffers = &commandBuffer;

            vkResetFences(m_Instance.device, 1, &context->GetFence());
            if (vkQueueSubmit(m_Instance.graphicsQueue, 1, &submitInfo, context->GetFence()) == VK_SUCCESS)
            {
                context->IncrementInFlightIndex();
            }
        }

        return ret;
    }
}

#undef max
bool XeSSRenderAPI_Vulkan::ValidAndWaitCmdFinished(xess_context_handle_t hContext) 
{
    std::lock_guard<std::mutex> lock(m_ContextStatesMutex);
    int contexIdx = FindContextStateIndex(hContext);
    if (contexIdx < 0)
        return false;

    ContextState_VK* context = (ContextState_VK*)m_ContextStates[contexIdx];
    vkWaitForFences(m_Instance.device, 1, &context->GetFence(), VK_TRUE, std::numeric_limits<uint64_t>::max());

    return true; 
}

void XeSSRenderAPI_Vulkan::ConfigPlugin(IUnityGraphicsVulkan* vulkan)
{
    UnityVulkanPluginEventConfig config;
    config.renderPassPrecondition = kUnityVulkanRenderPass_EnsureInside;
    config.graphicsQueueAccess = kUnityVulkanGraphicsQueueAccess_Allow;
    config.flags = kUnityVulkanEventConfigFlag_SyncWorkerThreads | kUnityVulkanEventConfigFlag_ModifiesCommandBuffersState | kUnityVulkanEventConfigFlag_FlushCommandBuffers;
    vulkan->ConfigureEvent(1, &config);
}

void XeSSRenderAPI_Vulkan::CreateResources()
{
    ContextState_VK::SetUnityVulkanInstance(&m_Instance);

    VkCommandPoolCreateInfo cmdPoolInfo = {};
    cmdPoolInfo.sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO;
    cmdPoolInfo.queueFamilyIndex = m_Instance.queueFamilyIndex;
    cmdPoolInfo.flags = VK_COMMAND_POOL_CREATE_RESET_COMMAND_BUFFER_BIT;
    if (vkCreateCommandPool(m_Instance.device, &cmdPoolInfo, nullptr, &m_CommandPool) != VK_SUCCESS)
    {
        XESS_UNITY_LOG("create command pool failed.");
    }
    else
    {
        ContextState_VK::SetVkCommandPool(&m_CommandPool);
    }
}

void XeSSRenderAPI_Vulkan::ReleaseResources()
{
    ReleaseContextsResource();
    if (m_CommandPool)
    {
        vkDestroyCommandPool(m_Instance.device, m_CommandPool, nullptr);
    }
    ContextState_VK::SetUnityVulkanInstance(NULL);
    ContextState_VK::SetVkCommandPool(NULL);
}
#endif	// #if SUPPORT_VULKAN