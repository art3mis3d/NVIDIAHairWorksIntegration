﻿#pragma once

#ifdef _WIN32
#ifdef hwImpl
#define hwExport __declspec(dllexport)
#else
#define hwExport __declspec(dllimport)
#endif
#endif

typedef NvHair::Sdk                   hwSDK;
typedef NvHair::AssetId               hwAssetID;
typedef NvHair::InstanceId            hwInstanceID;
typedef NvHair::InstanceDescriptor    hwHairDescriptor;
typedef NvHair::ConversionSettings    hwConversionSettings;
typedef NvHair::TextureType::Enum     hwTextureType;

typedef gfsdk_float3            hwFloat3;
typedef gfsdk_float4            hwFloat4;
typedef gfsdk_dualquaternion    hwDQuaternion;
typedef gfsdk_float4x4          hwMatrix;
typedef uint32_t                hwHShader;      // H stands for Handle
typedef uint32_t                hwHAsset;       // 
typedef uint32_t                hwHInstance;    // 

typedef ID3D11Device                    hwDevice;
typedef ID3D11Texture2D                 hwTexture;
typedef ID3D11ShaderResourceView        hwSRV;
typedef ID3D11RenderTargetView          hwRTV;

typedef void(__stdcall* hwLogCallback)(const char*);
#define hwNullAssetID       NvHair::ASSET_ID_NULL
#define hwNullInstanceID    NvHair::INSTANCE_ID_NULL
#define hwNullHandle        0xFFFFFFFF
#define hwMaxLights         8


struct  hwShaderData;
struct  hwAssetData;
struct  hwInstanceData;
struct  hwLightData;
class   hwContext;


// Unity plugin callbacks
extern "C" 
{
	hwExport int            hwGetSDKVersion();
	hwExport bool           hwLoadHairWorks();
	hwExport void           hwUnloadHairWorks();

	hwExport bool           hwInitialize();
	hwExport void           hwFinalize();
	hwExport hwContext* hwGetContext();
	hwExport int            hwGetFlushEventID();
	hwExport void           hwSetLogCallback(hwLogCallback cb);

	hwExport hwHShader      hwShaderLoadFromFile(const char* path);
	hwExport void           hwShaderRelease(hwHShader sid);
	hwExport void           hwShaderReload(hwHShader sid);

	hwExport hwHAsset       hwAssetLoadFromFile(const char* path, float unit);
	hwExport void           hwAssetRelease(hwHAsset aid);
	hwExport void           hwAssetReload(hwHAsset aid);
	hwExport int            hwAssetGetNumBones(hwHAsset aid);
	hwExport const char* hwAssetGetBoneName(hwHAsset aid, int nth);
	hwExport void           hwAssetGetBoneIndices(hwHAsset aid, hwFloat4& o_indices);
	hwExport void           hwAssetGetBoneWeights(hwHAsset aid, hwFloat4& o_weight);
	hwExport void           hwAssetGetBindPose(hwHAsset aid, int nth, hwMatrix& o_mat);
	hwExport void           hwAssetGetDefaultDescriptor(hwHAsset aid, hwHairDescriptor& o_desc);

	hwExport hwHInstance    hwInstanceCreate(hwHAsset aid);
	hwExport void           hwInstanceRelease(hwHInstance iid);
	hwExport void           hwInstanceGetAssetID(hwHInstance iid);
	hwExport void           hwInstanceGetBounds(hwHInstance iid, hwFloat3* o_min, hwFloat3* o_max);
	hwExport void           hwInstanceGetDescriptor(hwHInstance iid, hwHairDescriptor* o_desc);
	hwExport void           hwInstanceSetDescriptor(hwHInstance iid, const hwHairDescriptor* desc);
	hwExport void           hwInstanceSetTexture(hwHInstance iid, hwTextureType type, hwTexture* tex);
	hwExport void           hwInstanceUpdateSkinningMatrices(hwHInstance iid, int num_bones, hwMatrix* matrices);
	hwExport void           hwInstanceUpdateSkinningDQs(hwHInstance iid, int num_bones, hwDQuaternion* dqs);

	hwExport void           hwBeginScene();
	hwExport void           hwEndScene();
	hwExport void			hwInitializeDepthStencil(BOOL flipComparison);
	hwExport void           hwSetViewProjection(const hwMatrix* view, const hwMatrix* proj, float fov);
	hwExport void           hwSetRenderTarget(hwTexture* framebuffer, hwTexture* depthbuffer);
	hwExport void           hwSetShader(hwHShader sid);
	hwExport void           hwSetLights(int num_lights, const hwLightData* lights);
	hwExport void			hwSetShadowTexture(ID3D11Resource* shadowTex);
	hwExport void			hwSetShadowParams(ID3D11Buffer* shadowCB);
	hwExport void			hwSetSphericalHarmonics(const hwFloat4* Ar, const hwFloat4* Ag, const hwFloat4* Ab, const hwFloat4* Br, const hwFloat4* Bg, const hwFloat4* Bb, const hwFloat4* C);
	hwExport void			hwSetReflectionProbe(ID3D11Resource* tex1, ID3D11Resource* tex2);
	hwExport void			hwSetGIParameters(const hwFloat4* Params);
	hwExport void           hwRender(hwHInstance iid);
	hwExport void           hwRenderShadow(hwHInstance iid);
	hwExport void           hwStepSimulation(float dt);
} // extern "C"
