#include <./../include/Nv/HairWorks/Shader/NvHairShaderCommon.h>

#define MaxLights               8

#define LightType_Spot          0
#define LightType_Directional   1
#define LightType_Point         2

struct LightData
{
    int4 type; // x: light type
    float4 position; // w: range
    float4 direction;
    float4 color;
    int4 angle; // spot light angle
};

// shadow matrices and other related parameters
struct ShadowParams
{
    row_major float4x4 worldToShadow[4];
    float4 shadowSplitSpheres[4];
    float4 shadowSplitSqRadii;
    float4 LightSplitsNear;
    float4 LightSplitsFar;
};


NV_HAIR_DECLARE_SHADER_RESOURCES(t0, t1, t2);
// Hair Textures
Texture2D g_rootHairColorTexture : register(t5);
Texture2D g_tipHairColorTexture : register(t6);
Texture2D g_specularTexture : register(t7);
Texture2D g_strandTexture : register(t8);
// Reflection Probe Textures
TextureCube g_reflectionProbe1 : register(t9);
TextureCube g_reflectionProbe2 : register(t10);
// Shadowmap
Texture2D g_shadowTexture : register(t11);
// Shadow Matrices
StructuredBuffer<ShadowParams> shadowParams : register(t12);

cbuffer cbPerFrame : register(b0)
{
    // Spherical Harmonics Data
    float4 shAr;
    float4 shAg;
    float4 shAb;
    float4 shBr;
    float4 shBg;
    float4 shBb;
    float4 shC;

    float4 gi_params; //	x: light probe intensity y: reflection probe intensity z: specular strength  w: probe blend amount

    int4 g_numLights; // x: num lights
    LightData g_lights[MaxLights];
    NvHair_ConstantBuffer g_hairConstantBuffer;
}

cbuffer UnityPerDraw
{
    float4x4 glstate_matrix_mvp;
    float4x4 glstate_matrix_modelview0;
    float4x4 glstate_matrix_invtrans_modelview0;
#define UNITY_MATRIX_MVP glstate_matrix_mvp
#define UNITY_MATRIX_MV glstate_matrix_modelview0
#define UNITY_MATRIX_IT_MV glstate_matrix_invtrans_modelview0

    float4x4 _Object2World;
    float4x4 _World2Object;
    float4 unity_LODFade; // x is the fade value ranging within [0,1]. y is x quantized into 16 levels
    float4 unity_WorldTransformParams; // w is usually 1.0, or -1.0 for odd-negative scale transforms
}

// Standard Trilinear Sampler
SamplerState texSampler : register(s0);
// Mip Sampler For Shadows
SamplerState shadowSampler : register(s1);

// From Unity cgincludes
inline float4 getCascadeWeights(float3 wpos, float z)
{
    float4 zNear = float4(z >= shadowParams[0].LightSplitsNear);
    float4 zFar = float4(z < shadowParams[0].LightSplitsFar);
    float4 weights = zNear * zFar;
    return weights;
}

// world to shadowmap split spheres. From Unity cgincludes
inline float4 getCascadeWeights_splitSpheres(float3 wpos)
{
    float3 fromCenter0 = wpos.xyz - shadowParams[0].shadowSplitSpheres[0].xyz;
    float3 fromCenter1 = wpos.xyz - shadowParams[0].shadowSplitSpheres[1].xyz;
    float3 fromCenter2 = wpos.xyz - shadowParams[0].shadowSplitSpheres[2].xyz;
    float3 fromCenter3 = wpos.xyz - shadowParams[0].shadowSplitSpheres[3].xyz;
    float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
    float4 weights = float4(distances2 < shadowParams[0].shadowSplitSqRadii);
    weights.yzw = saturate(weights.yzw - weights.xyz);
    return weights;
}

// world to shadowmap coordinates. from Unity cgincludes
inline float4 getShadowCoord(float4 wpos, float4 cascadeWeights)
{
    float3 sc0 = mul(wpos, shadowParams[0].worldToShadow[0]).xyz;
    float3 sc1 = mul(wpos, shadowParams[0].worldToShadow[1]).xyz;
    float3 sc2 = mul(wpos, shadowParams[0].worldToShadow[2]).xyz;
    float3 sc3 = mul(wpos, shadowParams[0].worldToShadow[3]).xyz;

    float4 shadowMapCoordinate = float4(sc0 * cascadeWeights[0] + sc1 * cascadeWeights[1] + sc2 * cascadeWeights[2] + sc3 * cascadeWeights[3], 1);
    float noCascadeWeights = 1 - dot(cascadeWeights, float4(1, 1, 1, 1));
    shadowMapCoordinate.z += noCascadeWeights;
    return shadowMapCoordinate;
}

// shperical harmonics/ light probes lighting
inline float3 ShadeSH9(float4 Ar, float4 Ag, float4 Ab, float4 Br, float4 Bg, float4 Bb, float4 C, float4 normal)
{
    float3 x1, x2, x3;

    x1.r = dot(Ar, normal);
    x1.g = dot(Ag, normal);
    x1.b = dot(Ab, normal);

    float4 vB = normal.xyzz * normal.yzzx;

    x2.r = dot(Br, vB);
    x2.g = dot(Bg, vB);
    x2.b = dot(Bb, vB);

    float vC = normal.x * normal.x - normal.y * normal.y;

    x3 = C.rgb * vC;

    if (shC.w == 1)
        return x1 + x2 + x3;
    else
        return float3(0, 0, 0);
}

// simple spec map to mip level
float GetSpecPowToMip(float3 specColor)
{
    float fSpecPow = dot(specColor, float3(0.2126f, 0.7152f, 0.0722f));


    uint width1;
    uint height1;

    uint width2;
    uint height2;

    g_reflectionProbe1.GetDimensions(width1, height1);

    g_reflectionProbe2.GetDimensions(width2, height2);

    uint nMips1 = floor(log2(max(width1, height1)));

    uint nMips2 = floor(log2(max(width2, height2)));

    uint nMips = min(nMips1, nMips2);

    return lerp(0, nMips, 1.0f - fSpecPow);
}

[earlydepthstencil]
float4 ps_main(NvHair_PixelShaderInput input) : SV_Target
{
    // Get Shader Attributes
    NvHair_ShaderAttributes attr = NvHair_GetShaderAttributes(input, g_hairConstantBuffer);
    // Flip Texture Coordinates vertically
    attr.texcoords.y = 1.0f - attr.texcoords.y;
    // Get Material
    NvHair_Material mat = g_hairConstantBuffer.defaultMaterial;

    // if using specular sample spec texture
    if (g_hairConstantBuffer.useSpecularTexture)
        mat.specularColor.rgb = g_specularTexture.SampleLevel(texSampler, attr.texcoords.xy, 0).rgb;

    // init output to black
    float4 r = float4(0, 0, 0, 1);

    // if set to debug mode return visualization.
    if (NvHair_VisualizeColor(g_hairConstantBuffer, mat, attr, r.rgb))
    {
        return r;
    }

    // sample hair color
    float3 hairColor = NvHair_SampleHairColorStrandTex(g_hairConstantBuffer, mat, texSampler, g_rootHairColorTexture, g_tipHairColorTexture, g_strandTexture, attr.texcoords).rgb;

    // convert worldspace coordinates to shadowmap uv coordinates
    float4 cascadeWeights = getCascadeWeights_splitSpheres(attr.P.xyz);
    float4 shadowCoords = getShadowCoord(float4(attr.P.xyz, 1), cascadeWeights);
    
    // sample shadow map and perform pcf filtering
    float filteredDepth = NvHair_ShadowFilterDepth(g_shadowTexture, shadowSampler, shadowCoords.xy, shadowCoords.z, 1.0);
    float shadow = NvHair_ShadowLitFactor(g_hairConstantBuffer, mat, filteredDepth);

    // for each light
    for (int i = 0; i < g_numLights.x; i++)
    {
        // init some values
        float3 Lcolor = g_lights[i].color.rgb;
        float3 Ldir = float3(0, 0, 0);
        float atten = 1.0;

        // if directional light
        if (g_lights[i].type.x == LightType_Directional)
        {
            Ldir = g_lights[i].direction.xyz;
            // multiply shadow factor
            Lcolor *= shadow;
        }
        else
        {
            // point + spot light stuff
            float range = g_lights[i].position.w;
            float3 diff = g_lights[i].position.xyz - attr.P;
            Ldir = normalize(diff);
            atten = max(1.0f - dot(diff, diff) / (range * range), 0.0);

            // Spot Light Attenuation
            // Does not use cookie texture but this approximation is quite accurate
            if (g_lights[i].type.x == LightType_Spot)
            {
                float spotEffect = dot(g_lights[i].direction.xyz, -Ldir);

                // Outside spot cone
                if ((spotEffect - 0.02) < cos(radians((uint) g_lights[i].angle.x / 2)))
                {
                    float angleDif = cos(radians((uint) g_lights[i].angle.x / 2)) - (spotEffect - 0.02);

                    // Fade light smoothly near edge of cone
                    if (angleDif <= 0.02)
                    {

                        atten = lerp(atten, 0, angleDif * 50);
                    }
                    // Cut Off light outside cone
                    else
                        atten = 0;
                }
            }
        }

        // calc diffuse lighting
        float diffuse = NvHair_ComputeHairDiffuseShading(Ldir, attr.T, attr.N, mat.diffuseScale, mat.diffuseBlend);
        // calc specular lighting
        float specular = NvHair_ComputeHairSpecularShading(Ldir, attr, mat);

        // add some glint to ambient light
        float GlintAmbient = 0;

        // if using glint
        if (mat.glintStrength > 0)
        {
            // calc glint lighting
            float glint = NvHair_ComputeHairGlint(g_hairConstantBuffer, mat, attr);
            specular *= lerp(1.0, glint, mat.glintStrength);

            // add some glint to ambient light
            float Luminance = dot(Lcolor, float3(0.3, 0.5, 0.2)); // Copied from HairWorks viewer.
            GlintAmbient = mat.glintStrength * glint * Luminance;
        }

        // add direct plus ambient light to output
        r.rgb += ((hairColor.rgb * diffuse) + (specular * mat.specularColor.rgb)) * Lcolor.rgb * atten + GlintAmbient * atten * hairColor.rgb;
    }

    // calc transparency
    r.a = NvHair_ComputeAlpha(g_hairConstantBuffer, mat, attr);

    // add spherical harmonics or light probes
    r.rgb += (hairColor * ShadeSH9(shAr, shAg, shAb, shBr, shBg, shBb, shC, float4(attr.N, 1)) * gi_params.x);

    // calc mip level to use
    float mip = GetSpecPowToMip(mat.specularColor.rgb * gi_params.z);

    // sample reflection probes
    float3 probe1 = g_reflectionProbe1.SampleLevel(texSampler, attr.N, mip).rgb;
    float3 probe2 = g_reflectionProbe1.SampleLevel(texSampler, attr.N, mip).rgb;

    // add reflection probes contribution
    r.rgb += (hairColor * lerp(probe1, probe2, gi_params.w) * gi_params.y);
    
    return r;
}

