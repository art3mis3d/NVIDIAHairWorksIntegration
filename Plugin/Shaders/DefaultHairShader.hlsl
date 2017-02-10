#include "GFSDK_HairWorks_ShaderCommon.h" 

#define MaxLights               8

#define LightType_Spot          0
#define LightType_Directional   1
#define LightType_Point         2

struct LightData
{
    int4 type;          // x: light type
    float4 position;    // w: range
    float4 direction;
    float4 color;
};


GFSDK_HAIR_DECLARE_SHADER_RESOURCES(t0, t1, t2);

Texture2D	g_rootHairColorTexture	: register(t3);
Texture2D	g_tipHairColorTexture	: register(t4);
Texture2D   g_specularTexture       : register(t5);

cbuffer cbPerFrame : register(b0)
{
	float4 shAr;
	float4 shAg;
	float4 shAb;
	float4 shBr;
	float4 shBg;
	float4 shBb;
	float4 shC;
    int4                        g_numLights;        // x: num lights
    LightData                   g_lights[MaxLights];
    GFSDK_Hair_ConstantBuffer   g_hairConstantBuffer;
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


SamplerState texSampler: register(s0);


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

	float vC = normal.x*normal.x - normal.y*normal.y;

	x3 = C.rgb * vC;

	if (shC.w == 1)
		return x1 + x2 + x3;
	else return float3 (0, 0, 0);
}

[earlydepthstencil]
float4 ps_main(GFSDK_Hair_PixelShaderInput input) : SV_Target
{
    GFSDK_Hair_ShaderAttributes attr = GFSDK_Hair_GetShaderAttributes(input, g_hairConstantBuffer);
    GFSDK_Hair_Material mat = g_hairConstantBuffer.defaultMaterial;

	if (g_hairConstantBuffer.useSpecularTexture)
		mat.specularColor.rgb = g_specularTexture.SampleLevel(texSampler, attr.texcoords.xy, 0).rgb;

	float4 r = float4 (0, 0, 0, 1);

    if (GFSDK_Hair_VisualizeColor(g_hairConstantBuffer, mat, attr, r.rgb)) {
        return r;
    }

    float3 hairColor = GFSDK_Hair_SampleHairColorTex(g_hairConstantBuffer, mat, texSampler, g_rootHairColorTexture, g_tipHairColorTexture, attr.texcoords.xyz).rgb;

    for (int i = 0; i < g_numLights.x; i++)
    {
        float3 Lcolor = g_lights[i].color.rgb;
        float3 Ldir;
        float atten = 1.0;
        if (g_lights[i].type.x == LightType_Directional) {
            Ldir = g_lights[i].direction.xyz;
        }
        else if (g_lights[i].type.x == LightType_Point) {
            float range = g_lights[i].position.w;
            float3 diff = g_lights[i].position.xyz - attr.P;
            Ldir = normalize(diff);
            atten = max(1.0f - dot(diff, diff) / (range*range), 0.0);
        }

		float diffuse = GFSDK_Hair_ComputeHairDiffuseShading(Ldir, attr.T, attr.N, mat.diffuseScale, mat.diffuseBlend);
		float specular = GFSDK_Hair_ComputeHairSpecularShading(Ldir, attr, mat);

		float GlintAmbient = 0;

		if (mat.glintStrength > 0)
		{
			float glint = GFSDK_Hair_ComputeHairGlint(g_hairConstantBuffer, mat, attr);
			specular *= lerp(1.0, glint, mat.glintStrength);

			float Luminance = dot(Lcolor, float3(0.3, 0.5, 0.2));	// Copied from HairWorks viewer.
			GlintAmbient = mat.glintStrength * glint * Luminance;
		}

		r.a = GFSDK_Hair_ComputeAlpha(g_hairConstantBuffer, mat, attr);
		r.rgb += ((hairColor  * diffuse) + (specular * mat.specularColor)) * Lcolor * atten + GlintAmbient * atten * hairColor;
    }
    //r.rgb = saturate(attr.N.xyz)*0.5+0.5;
	r.rgb += (hairColor * ShadeSH9(shAr, shAg, shAb, shBr, shBg, shBb, shC, float4(attr.N, 1)));
    return r;
}

