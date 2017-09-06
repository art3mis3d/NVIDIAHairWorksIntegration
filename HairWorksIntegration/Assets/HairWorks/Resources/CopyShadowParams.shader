Shader "Hidden/CopyShadowParams"
{
	SubShader
	{
		Pass
	{
		CGPROGRAM
		#pragma target 5.0
		#pragma only_renderers d3d11
		#pragma enable_d3d11_debug_symbols
		#pragma vertex vert
		#pragma fragment frag
		#include "UnityCG.cginc"

			struct ShadowParams
			{
				float4x4 worldToShadow[4];
				float4 shadowSplitSpheres[4];
				float4 shadowSplitSqRadii;
				float4 LightSplitsNear;
				float4 LightSplitsFar;
			};

			RWStructuredBuffer<ShadowParams> _ShadowParams : register(u1);

			float4 vert() : SV_POSITION
			{
				return float4(0, 0, 0, 1);
			}

			fixed4 frag() : SV_Target
			{
				[unroll]
				for (int i = 0; i < 4; i++)
				{
					_ShadowParams[0].worldToShadow[i] = unity_WorldToShadow[i];
					_ShadowParams[0].shadowSplitSpheres[i] = unity_ShadowSplitSpheres[i];
				}

				_ShadowParams[0].shadowSplitSqRadii = unity_ShadowSplitSqRadii;
				_ShadowParams[0].LightSplitsNear = _LightSplitsNear;
				_ShadowParams[0].LightSplitsFar = _LightSplitsFar;

				return float4(0, 0, 0, 0);
			}
			ENDCG
		}
	}
}