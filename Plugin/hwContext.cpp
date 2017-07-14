#include "pch.h"
#include "hwInternal.h"
#include "hwContext.h"

#if defined(_M_IX86)
#define hwSDKDLL "NvHairWorksDx11.win32.dll"
#elif defined(_M_X64)
#define hwSDKDLL "NvHairWorksDx11.win64.dll"
#endif

bool operator==(const hwConversionSettings &a, const hwConversionSettings &b)
{
#define cmp(V) a.V==b.V
	return cmp(m_targetUpAxisHint) && cmp(m_targetHandednessHint) && cmp(m_conversionMatrix) && cmp(m_targetSceneUnit);
#undef cmp
}

bool hwFileToString(std::string &o_buf, const char *path)
{
    std::ifstream f(path, std::ios::binary);
    if (!f) { return false; }
    f.seekg(0, std::ios::end);
    o_buf.resize(f.tellg());
    f.seekg(0, std::ios::beg);
    f.read(&o_buf[0], o_buf.size());
    return true;
}


hwSDK *g_hw_sdk = nullptr;

hwSDK* hwContext::loadSDK()
{
    if (g_hw_sdk) {
        return g_hw_sdk;
    }

    char path[MAX_PATH] = {0};
    if(path[0] == 0) {
        // get path to this module
        HMODULE mod = 0;
        ::GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, (LPCSTR)&hwInitialize, &mod);
        DWORD size = ::GetModuleFileNameA(mod, path, sizeof(path));
        for (int i = size - 1; i >= 0; --i) {
            if (path[i] == '\\') {
                path[i + 1] = '\0';
                std::strncat(path, hwSDKDLL, MAX_PATH);
                break;
            }
        }
    }
	g_hw_sdk = NvHair::loadSdk(path, NV_HAIR_VERSION, nullptr, nullptr);
    hwLog("hwContext::loadSDK(): %s (%s)\n", g_hw_sdk ? "succeeded" : "failed", path);
    return g_hw_sdk;
}

void hwContext::unloadSDK()
{
    if (g_hw_sdk) {
        g_hw_sdk->release();
        g_hw_sdk = nullptr;
        hwLog("hwContext::unloadSDK()\n");
    }
}

hwContext::hwContext()
{
}

hwContext::~hwContext()
{
    finalize();
}

bool hwContext::valid() const
{
    return m_d3ddev!=nullptr && m_d3dctx!=nullptr && g_hw_sdk!=nullptr;
}

bool hwContext::initialize(hwDevice *d3d_device)
{
	if (d3d_device == nullptr) { return false; }

	g_hw_sdk = loadSDK();
	if (g_hw_sdk != nullptr) {
		hwLog("GFSDK_LoadHairSDK() succeeded.\n");
	}
	else {
		hwLog("GFSDK_LoadHairSDK() failed.\n");
		return false;
	}

	m_d3ddev = (ID3D11Device*)d3d_device;
	m_d3ddev->GetImmediateContext(&m_d3dctx);

	m_dev_handle = NvCo::Dx11Type::wrap((ID3D11Device*)d3d_device);
	m_ctx_handle = NvCo::Dx11Type::wrap(m_d3dctx);

	if (NV_SUCCEEDED(g_hw_sdk->initRenderResources(m_dev_handle, m_ctx_handle))) {
		hwLog("GFSDK_HairSDK::InitRenderResources() succeeded.\n");
	}
	else {
		hwLog("GFSDK_HairSDK::InitRenderResources() failed.\n");
		finalize();
		return false;
	}

	if (NV_SUCCEEDED(g_hw_sdk->setCurrentContext(m_ctx_handle))) {
		hwLog("GFSDK_HairSDK::SetCurrentContext() succeeded.\n");
	}
	else {
		hwLog("GFSDK_HairSDK::SetCurrentContext() failed.\n");
		finalize();
		return false;
	}

	{
		CD3D11_DEPTH_STENCIL_DESC desc;

		m_d3ddev->CreateDepthStencilState(&desc, &m_rs_enable_depth);
	}
	{
		// create constant buffer for hair rendering pixel shader
		D3D11_BUFFER_DESC desc;
		desc.Usage = D3D11_USAGE_DYNAMIC;
		desc.ByteWidth = sizeof(hwConstantBuffer);
		desc.StructureByteStride = 0;
		desc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
		desc.MiscFlags = 0;
		desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
		m_d3ddev->CreateBuffer(&desc, 0, &m_rs_constant_buffer);
	}

	return true;
}

void hwContext::finalize()
{
    for (auto &i : m_instances) { instanceRelease(i.handle); }
    m_instances.clear();

    for (auto &i : m_assets) { assetRelease(i.handle); }
    m_assets.clear();

    for (auto &i : m_shaders) { shaderRelease(i.handle); }
    m_shaders.clear();

    for (auto &i : m_srvtable) { i.second->Release(); }
    m_srvtable.clear();

    for (auto &i : m_rtvtable) { i.second->Release(); }
    m_rtvtable.clear();

	if (reflectionSRV1)
	{
		reflectionSRV1->Release();
		reflectionSRV1 = nullptr;
	}

	if (reflectionSRV2)
	{
		reflectionSRV2->Release();
		reflectionSRV2 = nullptr;
	}

	if (shadowSRV)
	{
		shadowSRV->Release();
		shadowSRV = nullptr;
	}

    if (m_rs_enable_depth) {
        m_rs_enable_depth->Release();
        m_rs_enable_depth = nullptr;
    }

    if (m_d3dctx)
    {
        m_d3dctx->Release();
        m_d3dctx = nullptr;
    }
}

void hwContext::move(hwContext &from)
{
#define mov(V) V=from.V; from.V=decltype(V)();

    mov(m_d3dctx);
    mov(m_d3ddev);

    mov(m_shaders);
    mov(m_assets);
    mov(m_instances);
    mov(m_srvtable);
    mov(m_rtvtable);
    //mov(m_commands);

    mov(m_rs_enable_depth);
    mov(m_rs_constant_buffer);
    //mov(m_cb);

#undef mov
}


hwShaderData& hwContext::newShaderData()
{
    auto i = std::find_if(m_shaders.begin(), m_shaders.end(), [](const hwShaderData &v) { return !v; });
    if (i != m_shaders.end()) { return *i; }

    hwShaderData tmp;
    tmp.handle = m_shaders.size();
    m_shaders.push_back(tmp);
    return m_shaders.back();
}

hwHShader hwContext::shaderLoadFromFile(const std::string &path)
{
    {
        auto i = std::find_if(m_shaders.begin(), m_shaders.end(), [&](const hwShaderData &v) { return v.path == path; });
        if (i != m_shaders.end() && i->ref_count > 0) {
            ++i->ref_count;
            return i->handle;
        }
    }

    std::string bin;
    if (!hwFileToString(bin, path.c_str())) {
        hwLog("failed to load shader (%s)\n", path.c_str());
        return hwNullHandle;
    }

    hwShaderData& v = newShaderData();
    v.path = path;
    if (SUCCEEDED(m_d3ddev->CreatePixelShader(&bin[0], bin.size(), nullptr, &v.shader))) {
        v.ref_count = 1;
        hwLog("CreatePixelShader(%s) : %d succeeded.\n", path.c_str(), v.handle);
        return v.handle;
    }
    else {
        hwLog("CreatePixelShader(%s) failed.\n", path.c_str());
    }
    return hwNullHandle;
}

void hwContext::shaderRelease(hwHShader hs)
{
    if (hs >= m_shaders.size()) { return; }

    auto &v = m_shaders[hs];
    if (v.ref_count > 0 && --v.ref_count == 0) {
        v.shader->Release();
        v.invalidate();
        hwLog("shaderRelease(%d)\n", hs);
    }
}

void hwContext::shaderReload(hwHShader hs)
{
    if (hs >= m_shaders.size()) { return; }

    auto &v = m_shaders[hs];
    // release existing shader
    if (v.shader) {
        v.shader->Release();
        v.shader = nullptr;
    }

    // reload
    std::string bin;
    if (!hwFileToString(bin, v.path.c_str())) {
        hwLog("failed to reload shader (%s)\n", v.path.c_str());
        return;
    }
    if (SUCCEEDED(m_d3ddev->CreatePixelShader(&bin[0], bin.size(), nullptr, &v.shader))) {
        hwLog("CreatePixelShader(%s) : %d reloaded.\n", v.path.c_str(), v.handle);
    }
    else {
        hwLog("CreatePixelShader(%s) failed to reload.\n", v.path.c_str());
    }
}


hwAssetData& hwContext::newAssetData()
{
    auto i = std::find_if(m_assets.begin(), m_assets.end(), [](const hwAssetData &v) { return !v; });
    if (i != m_assets.end()) { return *i; }

    hwAssetData tmp;
    tmp.handle = m_assets.size();
    m_assets.push_back(tmp);
    return m_assets.back();
}

hwHAsset hwContext::assetLoadFromFile(const std::string &path, const hwConversionSettings *_settings)
{
    hwConversionSettings settings;
    if (_settings != nullptr) { settings = *_settings; }

    {
        auto i = std::find_if(m_assets.begin(), m_assets.end(),
            [&](const hwAssetData &v) { return v.path == path && v.settings==settings; });
        if (i != m_assets.end() && i->ref_count > 0) {
            ++i->ref_count;
            return i->aid;
        }
    }

    hwAssetData& v = newAssetData();
    v.settings = settings;
    v.path = path;
	NvCo::StdCFileReadStream stream(path.c_str());
	if (NV_SUCCEEDED(g_hw_sdk->loadAsset(&stream, v.aid, nullptr, &settings))) {
        v.ref_count = 1;

        hwLog("GFSDK_HairSDK::LoadHairAssetFromFile(\"%s\") : %d succeeded.\n", path.c_str(), v.handle);
        return v.handle;
    }
    else {
        hwLog("GFSDK_HairSDK::LoadHairAssetFromFile(\"%s\") failed.\n", path.c_str());
    }
    return hwNullHandle;
}

void hwContext::assetRelease(hwHAsset ha)
{
	if (ha >= m_assets.size()) { return; }

	auto &v = m_assets[ha];
	if (v.ref_count > 0 && --v.ref_count == 0) {
		g_hw_sdk->freeAsset(v.aid);
		v.invalidate();
	}
}

void hwContext::assetReload(hwHAsset ha)
{
    if (ha >= m_assets.size()) { return; }

    auto &v = m_assets[ha];
    // release existing asset
	g_hw_sdk->freeAsset(v.aid);
	v.aid = hwNullAssetID;

	// reload
	NvCo::StdCFileReadStream stream(v.path.c_str());
	if (NV_SUCCEEDED(g_hw_sdk->loadAsset(&stream, v.aid, nullptr, &v.settings))) {
        hwLog("GFSDK_HairSDK::LoadHairAssetFromFile(\"%s\") : %d reloaded.\n", v.path.c_str(), v.handle);
    }
    else {
        hwLog("GFSDK_HairSDK::LoadHairAssetFromFile(\"%s\") failed to reload.\n", v.path.c_str());
    }
}

int hwContext::assetGetNumBones(hwHAsset ha) const
{
	if (ha >= m_assets.size()) { return 0; }

	return g_hw_sdk->getNumBones(m_assets[ha].aid);
}

const char* hwContext::assetGetBoneName(hwHAsset ha, int nth) const
{
    static char tmp[256];
    if (ha >= m_assets.size()) { tmp[0] = '\0'; return tmp; }

	if (!NV_SUCCEEDED(g_hw_sdk->getBoneName(m_assets[ha].aid, nth, tmp))) {
        hwLog("GFSDK_HairSDK::GetBoneName(%d) failed.\n", ha);
    }
    return tmp;
}

void hwContext::assetGetBoneIndices(hwHAsset ha, hwFloat4 &o_indices) const
{
    if (ha >= m_assets.size()) { return; }

	if (!NV_SUCCEEDED(g_hw_sdk->getBoneIndices(m_assets[ha].aid, &o_indices))) {
        hwLog("GFSDK_HairSDK::GetBoneIndices(%d) failed.\n", ha);
    }
}

void hwContext::assetGetBoneWeights(hwHAsset ha, hwFloat4 &o_weight) const
{
    if (ha >= m_assets.size()) { return; }

	if (!NV_SUCCEEDED(g_hw_sdk->getBoneWeights(m_assets[ha].aid, &o_weight))) {
        hwLog("GFSDK_HairSDK::GetBoneWeights(%d) failed.\n", ha);
    }
}

void hwContext::assetGetBindPose(hwHAsset ha, int nth, hwMatrix &o_mat)
{
    if (ha >= m_assets.size()) { return; }

	if (!NV_SUCCEEDED(g_hw_sdk->getBindPose(m_assets[ha].aid, nth, &o_mat))) {
        hwLog("GFSDK_HairSDK::GetBindPose(%d, %d) failed.\n", ha, nth);
    }
}

void hwContext::assetGetDefaultDescriptor(hwHAsset ha, hwHairDescriptor &o_desc) const
{
    if (ha >= m_assets.size()) { return; }

	if (!NV_SUCCEEDED(g_hw_sdk->getInstanceDescriptorFromAsset(m_assets[ha].aid, o_desc))) {
        hwLog("GFSDK_HairSDK::CopyInstanceDescriptorFromAsset(%d) failed.\n", ha);
    }
}

hwInstanceData& hwContext::newInstanceData()
{
    auto i = std::find_if(m_instances.begin(), m_instances.end(), [](const hwInstanceData &v) { return !v; });
    if (i != m_instances.end()) { return *i; }

    hwInstanceData tmp;
    tmp.handle = m_instances.size();
    m_instances.push_back(tmp);
    return m_instances.back();
}

hwHInstance hwContext::instanceCreate(hwHAsset ha)
{
	if (ha >= m_assets.size()) { return hwNullHandle; }

	hwInstanceData& v = newInstanceData();
	v.hasset = ha;
	if (NV_SUCCEEDED(g_hw_sdk->createInstance(m_assets[ha].aid, v.iid))) {
		hwLog("GFSDK_HairSDK::CreateHairInstance(%d) : %d succeeded.\n", ha, v.handle);
	}
	else
	{
		hwLog("GFSDK_HairSDK::CreateHairInstance(%d) failed.\n", ha);
	}
	return v.handle;
}

void hwContext::instanceRelease(hwHInstance hi)
{
    if (hi >= m_instances.size()) { return; }
    auto &v = m_instances[hi];

	if (NV_SUCCEEDED(g_hw_sdk->freeInstance(v.iid))) {
        hwLog("GFSDK_HairSDK::FreeHairInstance(%d) succeeded.\n", hi);
    }
    else {
        hwLog("GFSDK_HairSDK::FreeHairInstance(%d) failed.\n", hi);
    }
    v.invalidate();
}

void hwContext::instanceGetBounds(hwHInstance hi, hwFloat3 &o_min, hwFloat3 &o_max) const
{
    if (hi >= m_instances.size()) { return; }
    auto &v = m_instances[hi];

	if (!NV_SUCCEEDED(g_hw_sdk->getBounds(v.iid, o_min, o_max, false)))
	{
        hwLog("GFSDK_HairSDK::GetBounds(%d) failed.\n", hi);
    }
}

void hwContext::instanceGetDescriptor(hwHInstance hi, hwHairDescriptor &desc) const
{
    if (hi >= m_instances.size()) { return; }
    auto &v = m_instances[hi];

	if (!NV_SUCCEEDED(g_hw_sdk->getInstanceDescriptor(v.iid, desc)))
	{
        hwLog("GFSDK_HairSDK::CopyCurrentInstanceDescriptor(%d) failed.\n", hi);
    }
}

void hwContext::instanceSetDescriptor(hwHInstance hi, const hwHairDescriptor &desc)
{
    if (hi >= m_instances.size()) { return; }
    auto &v = m_instances[hi];

	if (!NV_SUCCEEDED(g_hw_sdk->updateInstanceDescriptor(v.iid, desc)))
	{
        hwLog("GFSDK_HairSDK::UpdateInstanceDescriptor(%d) failed.\n", hi);
    }
}

void hwContext::instanceSetTexture(hwHInstance hi, hwTextureType type, hwTexture *tex)
{
	if (hi >= m_instances.size()) { return; }
	auto &v = m_instances[hi];

	if (!tex)
	{
		if (!NV_SUCCEEDED(g_hw_sdk->setTexture(v.iid, type, nvidia::Common::ApiHandle::getNull())))
		{
			hwLog("GFSDK_HairSDK::SetTextureSRV(%d, %d) failed.\n", hi, type);
		}

		return;
	}

	D3D11_TEXTURE2D_DESC texDesc;
	tex->GetDesc(&texDesc);
	ID3D11ShaderResourceView* srv;
	D3D11_SHADER_RESOURCE_VIEW_DESC SRVDesc;
	ZeroMemory(&SRVDesc, sizeof(SRVDesc));
	SRVDesc.Format = texDesc.Format;
	SRVDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
	SRVDesc.Texture2D.MostDetailedMip = 0;
	SRVDesc.Texture2D.MipLevels = texDesc.MipLevels;
	m_d3ddev->CreateShaderResourceView(tex, &SRVDesc, &srv);

	if (!srv || !NV_SUCCEEDED(g_hw_sdk->setTexture(v.iid, type, nvidia::Common::Dx11Type::wrap(srv))))
	{
		hwLog("GFSDK_HairSDK::SetTextureSRV(%d, %d) failed.\n", hi, type);
	}
}

void hwContext::setShadowTexture(ID3D11Resource *shadowTex)
{
	if (shadowSRV)
	{
		shadowSRV->Release();
		shadowSRV = nullptr;
	}

	if (!shadowTex)
	{
		return;
	}

	D3D11_TEXTURE2D_DESC texDesc;

	shadowTex->QueryInterface(&shadowTexture);

	shadowTexture->GetDesc(&texDesc);
	
	D3D11_SHADER_RESOURCE_VIEW_DESC SRVDesc;
	ZeroMemory(&SRVDesc, sizeof(SRVDesc));
	SRVDesc.Format = DXGI_FORMAT_R32_FLOAT;
	SRVDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
	SRVDesc.Texture2D.MostDetailedMip = 0;
	SRVDesc.Texture2D.MipLevels = 1;
	HRESULT hr = m_d3ddev->CreateShaderResourceView(shadowTexture, &SRVDesc, &shadowSRV);

	if (!SUCCEEDED(hr))
	{
		hwLog("Create Shadow SRV Failed!");
	}
}

void hwContext::instanceUpdateSkinningMatrices(hwHInstance hi, int num_bones, hwMatrix *matrices)
{
	if (matrices == nullptr) { return; }
	if (hi >= m_instances.size()) { return; }
	auto &v = m_instances[hi];

	if (!NV_SUCCEEDED(g_hw_sdk->updateSkinningMatrices(v.iid, num_bones, matrices)))
	{
		hwLog("GFSDK_HairSDK::UpdateSkinningMatrices(%d) failed.\n", hi);
	}
}

void hwContext::instanceUpdateSkinningDQs(hwHInstance hi, int num_bones, hwDQuaternion *dqs)
{
    if (dqs == nullptr) { return; }
    if (hi >= m_instances.size()) { return; }
    auto &v = m_instances[hi];

	if (!NV_SUCCEEDED(g_hw_sdk->updateSkinningDqs(v.iid, num_bones, dqs)))
	{
        hwLog("GFSDK_HairSDK::UpdateSkinningDQs(%d) failed.\n", hi);
    }
}


void hwContext::beginScene()
{
    m_mutex.lock();
}

void hwContext::endScene()
{
    m_mutex.unlock();
}

void hwContext::initializeDepthStencil(BOOL flipComparison)
{
	CD3D11_DEPTH_STENCIL_DESC desc;

	if (flipComparison)
	{
		desc.DepthEnable = TRUE;
		desc.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ALL;
		desc.DepthFunc = D3D11_COMPARISON_GREATER;

		desc.StencilEnable = FALSE;

		desc.StencilReadMask = D3D11_DEFAULT_STENCIL_READ_MASK;
		desc.StencilWriteMask = D3D11_DEFAULT_STENCIL_WRITE_MASK;


		desc.FrontFace.StencilFunc = D3D11_COMPARISON_ALWAYS;
		desc.FrontFace.StencilPassOp = D3D11_STENCIL_OP_KEEP;
		desc.FrontFace.StencilFailOp = D3D11_STENCIL_OP_KEEP;
		desc.FrontFace.StencilDepthFailOp = D3D11_STENCIL_OP_KEEP;

		desc.BackFace.StencilFunc = D3D11_COMPARISON_ALWAYS;
		desc.BackFace.StencilPassOp = D3D11_STENCIL_OP_KEEP;
		desc.BackFace.StencilFailOp = D3D11_STENCIL_OP_KEEP;
		desc.BackFace.StencilDepthFailOp = D3D11_STENCIL_OP_KEEP;
	}

		m_d3ddev->CreateDepthStencilState(&desc, &m_rs_enable_depth);
	
}

void hwContext::setShadowParams(void* shadowCB)
{
	shadowBuffer = static_cast<ID3D11Buffer*>(shadowCB);

	if (!shadowBuffer)
	{
		if (bufferSRV)
		{
			bufferSRV->Release();
			bufferSRV = nullptr;
		}
		return;
	}

	D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc;
	ZeroMemory(&srvDesc, sizeof(srvDesc));
	srvDesc.Format = DXGI_FORMAT_UNKNOWN;
	srvDesc.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
	srvDesc.Buffer.ElementWidth = 1;
	m_d3ddev->CreateShaderResourceView(shadowBuffer, &srvDesc, &bufferSRV);
}

void hwContext::pushDeferredCall(const DeferredCall &c)
{
    m_commands.push_back(c);
}

void hwContext::setRenderTarget(hwTexture *framebuffer, hwTexture *depthbuffer)
{
    pushDeferredCall([=]() {
        setRenderTargetImpl(framebuffer, depthbuffer);
    });
}

void hwContext::setViewProjection(const hwMatrix &view, const hwMatrix &proj, float fov)
{
    pushDeferredCall([=]() {
        setViewProjectionImpl(view, proj, fov);
    });
}

void hwContext::setShader(hwHShader hs)
{
    pushDeferredCall([=]() {
        setShaderImpl(hs);
    });
}

void hwContext::setLights(int num_lights, const hwLightData *lights_)
{
    std::array<hwLightData, hwMaxLights> lights;
    num_lights = std::min<int>(num_lights, hwMaxLights);
    std::copy(lights_, lights_ + num_lights, &lights[0]);
    pushDeferredCall([=]() {
        setLightsImpl(num_lights, &lights[0]);
    });

}

void hwContext::setSphericalHarmonics(const hwFloat4 &Ar, const hwFloat4 &Ag, const hwFloat4 &Ab, const hwFloat4 &Br, const hwFloat4 &Bg, const hwFloat4 &Bb, const hwFloat4 &C)
{
	pushDeferredCall([=]() {
		setSphericalHarmonicsImpl(Ar, Ag, Ab, Br, Bg, Bb, C);
	});
}

void hwContext::setGIParameters(const hwFloat4 &Params)
{
	pushDeferredCall([=]() {
		setGIParametersImpl(Params);
	});
}

void hwContext::setReflectionProbe(ID3D11Resource *tex1, ID3D11Resource *tex2)
{
	pushDeferredCall([=]() {
		setReflectionProbeImpl(tex1, tex2);
	});
}

void hwContext::render(hwHInstance hi)
{
    pushDeferredCall([=]() {
        renderImpl(hi);
    });
}

void hwContext::renderShadow(hwHInstance hi)
{
    pushDeferredCall([=]() {
        renderShadowImpl(hi);
    });
}

void hwContext::stepSimulation(float dt)
{
    std::unique_lock<std::mutex> lock(m_mutex);

    pushDeferredCall([=]() {
        stepSimulationImpl(dt);
    });
}


hwSRV* hwContext::getSRV(hwTexture *tex)
{
    {
        auto i = m_srvtable.find(tex);
        if (i != m_srvtable.end()) {
            return i->second;
        }
    }

    hwSRV *ret = nullptr;
    if (SUCCEEDED(m_d3ddev->CreateShaderResourceView(tex, nullptr, &ret))) {
        m_srvtable[tex] = ret;
    }
    return ret;
}

hwRTV* hwContext::getRTV(hwTexture *tex)
{
    {
        auto i = m_rtvtable.find(tex);
        if (i != m_rtvtable.end()) {
            return i->second;
        }
    }

    hwRTV *ret = nullptr;
    if (SUCCEEDED(m_d3ddev->CreateRenderTargetView(tex, nullptr, &ret))) {
        m_rtvtable[tex] = ret;
    }
    return ret;
}


void hwContext::setRenderTargetImpl(hwTexture *framebuffer, hwTexture *depthbuffer)
{
    // todo
}

void hwContext::setViewProjectionImpl(const hwMatrix &view, const hwMatrix &proj, float fov)
{
	D3D11_VIEWPORT dxViewport;
	UINT numViewports = 1;
	m_d3dctx->RSGetViewports(&numViewports, &dxViewport);

	NvHair::Viewport viewport;
	viewport.init(dxViewport.TopLeftX, dxViewport.TopLeftY, dxViewport.Width, dxViewport.Height);

	//g_hw_sdk->setViewProjection((const gfsdk_float4x4*)&view, (const gfsdk_float4x4*)&proj, GFSDK_HAIR_RIGHT_HANDED, fov) != GFSDK_HAIR_RETURN_OK

	if (!NV_SUCCEEDED(g_hw_sdk->setViewProjection(viewport, view, proj, nvidia::HairWorks::HandednessHint::RIGHT, fov)))
	{
		hwLog("GFSDK_HairSDK::SetViewProjection() failed.\n");
	}
}

void hwContext::setShaderImpl(hwHShader hs)
{
    if (hs >= m_shaders.size()) { return; }

    auto &v = m_shaders[hs];
    if (v.shader) {
        m_d3dctx->PSSetShader(v.shader, nullptr, 0);
    }
}

void hwContext::setLightsImpl(int num_lights, const hwLightData *lights)
{
    m_cb.num_lights = num_lights;
    std::copy(lights, lights + num_lights, m_cb.lights);
}

void hwContext::setSphericalHarmonicsImpl(const hwFloat4 &Ar, const hwFloat4 &Ag, const hwFloat4 &Ab, const hwFloat4 &Br, const hwFloat4 &Bg, const hwFloat4 &Bb, const hwFloat4 &C)
{
	m_cb.shAr = Ar;
	m_cb.shAg = Ag;
	m_cb.shAb = Ab;
	m_cb.shBr = Br;
	m_cb.shBg = Bg;
	m_cb.shBb = Bb;
	m_cb.shC = C;

}

void hwContext::setGIParametersImpl(const hwFloat4 &Params)
{
	m_cb.gi_params = Params;
}

void hwContext::setReflectionProbeImpl(ID3D11Resource *tex1, ID3D11Resource *tex2)
{
	if (!tex1)
	{
		if (reflectionSRV1)
		{
			reflectionSRV1->Release();
			reflectionSRV1 = nullptr;
		}
	}

	if (!tex2)
	{
		if (reflectionSRV2)
		{
			reflectionSRV2->Release();
			reflectionSRV2 = nullptr;
		}
	}

	if (!tex1 || !tex2)
		return;

	if (tex1 == reflectionTexture1 && tex2 == reflectionTexture2)
		return;
	
	ID3D11Texture2D* cubemap1;
	ID3D11Texture2D* cubemap2;

	reflectionTexture1 = tex1;
	reflectionTexture2 = tex2;

	reflectionTexture1->QueryInterface(&cubemap1);
	reflectionTexture2->QueryInterface(&cubemap2);

	D3D11_TEXTURE2D_DESC texDesc1;
	cubemap1->GetDesc(&texDesc1);

	D3D11_SHADER_RESOURCE_VIEW_DESC SRVDesc1;
	ZeroMemory(&SRVDesc1, sizeof(SRVDesc1));

	SRVDesc1.Format = texDesc1.Format;
	SRVDesc1.ViewDimension = D3D11_SRV_DIMENSION_TEXTURECUBE;
	SRVDesc1.Texture2D.MostDetailedMip = 0;
	SRVDesc1.Texture2D.MipLevels = texDesc1.MipLevels;

	D3D11_TEXTURE2D_DESC texDesc2;
	cubemap2->GetDesc(&texDesc2);

	D3D11_SHADER_RESOURCE_VIEW_DESC SRVDesc2;
	ZeroMemory(&SRVDesc2, sizeof(SRVDesc2));

	SRVDesc2.Format = texDesc2.Format;
	SRVDesc2.ViewDimension = D3D11_SRV_DIMENSION_TEXTURECUBE;
	SRVDesc2.Texture2D.MostDetailedMip = 0;
	SRVDesc2.Texture2D.MipLevels = texDesc2.MipLevels;

	m_d3ddev->CreateShaderResourceView(cubemap1, &SRVDesc1, &reflectionSRV1);
	m_d3ddev->CreateShaderResourceView(cubemap2, &SRVDesc2, &reflectionSRV2);

	if (cubemap1)
	{
		cubemap1 = nullptr;
	}

	if (cubemap2)
	{
		cubemap2 = nullptr;
	}
}

void hwContext::renderImpl(hwHInstance hi)
{
    if (hi >= m_instances.size()) { return; }
    auto &v = m_instances[hi];

	g_hw_sdk->preRender(1);

    // update constant buffer
    {
		g_hw_sdk->prepareShaderConstantBuffer(v.iid, m_cb.hw);

        D3D11_MAPPED_SUBRESOURCE MappedResource;
        m_d3dctx->Map(m_rs_constant_buffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &MappedResource);
        *((hwConstantBuffer*)MappedResource.pData) = m_cb;
        m_d3dctx->Unmap(m_rs_constant_buffer, 0);

        m_d3dctx->PSSetConstantBuffers(0, 1, &m_rs_constant_buffer);
    }

	// set texture sampler state
	{
		D3D11_SAMPLER_DESC samplerDesc;
		samplerDesc.AddressU = D3D11_TEXTURE_ADDRESS_WRAP;
		samplerDesc.AddressV = D3D11_TEXTURE_ADDRESS_WRAP;
		samplerDesc.AddressW = D3D11_TEXTURE_ADDRESS_WRAP;
		samplerDesc.BorderColor[0] = 0;
		samplerDesc.BorderColor[1] = 0;
		samplerDesc.BorderColor[2] = 0;
		samplerDesc.BorderColor[3] = 0;
		samplerDesc.ComparisonFunc = D3D11_COMPARISON_ALWAYS;
		samplerDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
		samplerDesc.MaxAnisotropy = 1;
		samplerDesc.MinLOD = 0;
		samplerDesc.MaxLOD = D3D11_FLOAT32_MAX;
		samplerDesc.MipLODBias = 0;
		ID3D11SamplerState* sampler;
		m_d3ddev->CreateSamplerState(&samplerDesc, &sampler);
		m_d3dctx->PSSetSamplers(0, 1, &sampler);

		if (sampler) {
			sampler->Release(); 
			sampler = nullptr;
		}
	}

	// set shadow sampler state
	{
		D3D11_SAMPLER_DESC shadowSamplerDesc;
		shadowSamplerDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
		shadowSamplerDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
		shadowSamplerDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
		shadowSamplerDesc.BorderColor[0] = 0;
		shadowSamplerDesc.BorderColor[1] = 0;
		shadowSamplerDesc.BorderColor[2] = 0;
		shadowSamplerDesc.BorderColor[3] = 0;
		//shadowSamplerDesc.ComparisonFunc = D3D11_COMPARISON_GREATER;
		//shadowSamplerDesc.Filter = D3D11_FILTER_COMPARISON_MIN_MAG_MIP_POINT;
		shadowSamplerDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;
		shadowSamplerDesc.MaxAnisotropy = 0;
		shadowSamplerDesc.MinLOD = 0;
		shadowSamplerDesc.MaxLOD = D3D11_FLOAT32_MAX;
		shadowSamplerDesc.MipLODBias = 0;
		ID3D11SamplerState* shadowSampler;
		m_d3ddev->CreateSamplerState(&shadowSamplerDesc, &shadowSampler);
		m_d3dctx->PSSetSamplers(1, 1, &shadowSampler);

		if (shadowSampler) {
			shadowSampler->Release();
			shadowSampler = nullptr;
		}
	}

    // set shader resource views
    {
		ID3D11ShaderResourceView* SRVs[NvHair::ShaderResourceType::COUNT_OF] = { nullptr, nullptr, nullptr, nullptr, nullptr };
		g_hw_sdk->getShaderResources(v.iid, NV_NULL, NvHair::ShaderResourceType::COUNT_OF, NvCo::Dx11Type::wrapPtr(SRVs));
		m_d3dctx->PSSetShaderResources(0, NvHair::ShaderResourceType::COUNT_OF, SRVs);

		ID3D11ShaderResourceView* ppTextureSRVs[4] = { nullptr, nullptr, nullptr, nullptr };

		NvHair::TextureType::Enum textureTypes[4] = { NvHair::TextureType::ROOT_COLOR , NvHair::TextureType::TIP_COLOR, NvHair::TextureType::SPECULAR, NvHair::TextureType::STRAND };

		if (NV_SUCCEEDED(g_hw_sdk->getTextures(v.iid, textureTypes, 4, NvCo::Dx11Type::wrapPtr(ppTextureSRVs))))
		{
			m_d3dctx->PSSetShaderResources(NvHair::ShaderResourceType::COUNT_OF, 4, ppTextureSRVs);
		}

		// set reflection probe
		m_d3dctx->PSSetShaderResources(9, 1, &reflectionSRV1);

		m_d3dctx->PSSetShaderResources(10, 1, &reflectionSRV2);

		// set shadow texture
		m_d3dctx->PSSetShaderResources(11, 1, &shadowSRV);

		// update shadow matrices buffer
		m_d3dctx->PSSetShaderResources(12, 1, &bufferSRV);
		
    }

    // render
	NvHair::ShaderSettings settings = NvHair::ShaderSettings(true, false);
	if (!NV_SUCCEEDED(g_hw_sdk->renderHairs(v.iid, &settings)))
	{
        hwLog("GFSDK_HairSDK::RenderHairs(%d) failed.\n", hi);
    }
    // render indicators
	g_hw_sdk->renderVisualization(v.iid);
}

void hwContext::renderShadowImpl(hwHInstance hi)
{
	if (hi >= m_instances.size()) { return; }
	auto &v = m_instances[hi];

	// set shader resource views
	{
		ID3D11ShaderResourceView* SRVs[NvHair::ShaderResourceType::COUNT_OF];
		g_hw_sdk->getShaderResources(v.iid, NV_NULL, NvHair::ShaderResourceType::COUNT_OF, NvCo::Dx11Type::wrapPtr(SRVs));
		m_d3dctx->PSSetShaderResources(0, NvHair::ShaderResourceType::COUNT_OF, SRVs);
	}

	auto settings = NvHair::ShaderSettings(false, true);
	if (!NV_SUCCEEDED(g_hw_sdk->renderHairs(v.iid, &settings)))
	{
		hwLog("GFSDK_HairSDK::RenderHairs(%d) failed.\n", hi);
	}
}

void hwContext::stepSimulationImpl(float dt)
{
	if (!NV_SUCCEEDED(g_hw_sdk->stepSimulation(dt, nullptr, true)))
	{
		hwLog("GFSDK_HairSDK::StepSimulation(%f) failed.\n", dt);
	}
}

void hwContext::flush()
{
	{
		std::unique_lock<std::mutex> lock(m_mutex);
		m_commands_back = m_commands;
		m_commands.clear();
	}

	m_d3dctx->OMSetDepthStencilState(m_rs_enable_depth, 0);
    for (auto& c : m_commands_back) {
        c();
    }
    m_commands_back.clear();
}

