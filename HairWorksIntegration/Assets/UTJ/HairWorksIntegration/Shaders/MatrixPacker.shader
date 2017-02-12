// Upgrade NOTE: replaced 'unity_World2Shadow' with 'unity_WorldToShadow'

Shader "Hidden/MatrixPacker"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = mul(UNITY_MATRIX_MVP, v.vertex);
				o.uv = v.uv;
				return o;
			}
			
			sampler2D _MainTex;

			inline int getIndexVertical(float coord, inout float previous)
			{
				if (coord <= 0.25f)
				{
					previous = 0.0f;
					return 0;
				}
				if (coord <= 0.5f)
				{
					previous = 0.25f;
					return 1;
				}
				if (coord <= 0.75f)
				{
					previous = 0.5f;
					return 2;
				}

				previous = 0.75f;
				return 3;
			}

			inline int getIndexHorizontal(float coord, float previous)
			{
				return (int)floor((((coord - previous) * 4.0f) * 3.0f));
			}

			inline int getFloatIndex(float coord)
			{
				if (coord <= 0.25f)
				{
					return 0;
				}
				if (coord <= 0.5f)
				{
					return 1;
				}
				if (coord <= 0.75f)
				{
					return 2;
				}

				return 3;
			}

			float frag (v2f i) : SV_Target
			{
				float previous = 0;
				int verticalIdx = getIndexVertical(i.uv.y, previous);
				int horizontalIdx = getIndexHorizontal(i.uv.y, previous);
				return unity_WorldToShadow[verticalIdx][horizontalIdx][getFloatIndex(i.uv.x)];
			}
			ENDCG
		}
	}
}
