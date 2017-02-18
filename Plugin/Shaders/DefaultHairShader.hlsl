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
	int4 angle;
};


GFSDK_HAIR_DECLARE_SHADER_RESOURCES(t0, t1, t2);

Texture2D	g_rootHairColorTexture	: register(t3);
Texture2D	g_tipHairColorTexture	: register(t4);
Texture2D   g_specularTexture       : register(t5);
TextureCube g_reflectionProbe1		: register(t6);
TextureCube g_reflectionProbe2		: register(t7);

cbuffer cbPerFrame : register(b0)
{
	float4 shAr;
	float4 shAg;
	float4 shAb;
	float4 shBr;
	float4 shBg;
	float4 shBb;
	float4 shC;
	float4 gi_params;	//	x: light probe intensity y: reflection probe intensity z: specular strength  w: probe blend amount
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

	//float roughness2 = pow(2.0f / (fSpecPow + 2.0f), 0.25f);

	return lerp(0, nMips, 1.0f - fSpecPow);
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
        float3 Ldir = float3(0,0,0);
        float atten = 1.0;
        if (g_lights[i].type.x == LightType_Directional) {
            Ldir = g_lights[i].direction.xyz;
        }
        else {
            float range = g_lights[i].position.w;
            float3 diff = g_lights[i].position.xyz - attr.P;
            Ldir = normalize(diff);
            atten = max(1.0f - dot(diff, diff) / (range*range), 0.0);

			// Spot Light Attenuation
			// Does not use cookie texture but approximation is quite accurate
			if (g_lights[i].type.x == LightType_Spot)
			{
				float spotEffect = dot(g_lights[i].direction.xyz, -Ldir);

				// Outside spot cone
				if ((spotEffect - 0.02) < cos(radians((uint)g_lights[i].angle.x / 2)))
				{
					float angleDif = cos(radians((uint)g_lights[i].angle.x / 2)) - (spotEffect - 0.02);

					// Fade light smoothly near edge of cone
					if (angleDif <= 0.02) {

						atten = lerp(atten, 0, angleDif * 50);
					}
					// Cut Off light outside cone
					else
						atten = 0;
				}
			}
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
		r.rgb += ((hairColor.rgb  * diffuse) + (specular * mat.specularColor.rgb)) * Lcolor.rgb * atten + GlintAmbient * atten * hairColor.rgb;
    }
    //r.rgb = saturate(attr.N.xyz)*0.5+0.5;
	r.rgb += (hairColor * ShadeSH9(shAr, shAg, shAb, shBr, shBg, shBb, shC, float4(attr.N, 1)) * gi_params.x);

	float mip = GetSpecPowToMip(mat.specularColor * gi_params.z);
	float3 probe1 = g_reflectionProbe1.SampleLevel(texSampler, attr.N, mip).rgb;
	float3 probe2 = g_reflectionProbe1.SampleLevel(texSampler, attr.N, mip).rgb;

	r.rgb += (hairColor * lerp(probe1, probe2, gi_params.w) * gi_params.y);
    return r;
}

