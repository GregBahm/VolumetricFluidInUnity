Shader "FlowingFluidParticleShader"
{
	Properties
	{
		_Lut("Lut", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 5.0

		#define Resolution 128

			#include "UnityCG.cginc"

			StructuredBuffer<float3> _ParticleBuffer;
			StructuredBuffer<float3> _MeshBuffer;

			sampler3D _dyeTexture;
			sampler3D _velocityTexture;
			sampler2D _Lut;

			float4x4 _Transform;

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float3 col : TEXCOORD0;
				float2 uvs : TEXCOORD1;
			};

			float _ParticleSize;

			v2f vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
			{
				float pixelOffset = (float)1 / Resolution;

				float3 basePos = _ParticleBuffer[inst];
				float3 meshPos = _MeshBuffer[id];

				float dye = tex3Dlod(_dyeTexture, float4(basePos, 0)).y;
				float3 velocity = tex3Dlod(_velocityTexture, float4(basePos, 0)).xyz;
				float force = length(velocity);
				//dye = pow(saturate(force - .1), 5);
				float scale = (1 - pow(1 - saturate(dye), 2)) * (pixelOffset / 2) * _ParticleSize;

				float sampleAbove = tex3Dlod(_dyeTexture, float4(basePos + float3(0, -pixelOffset, 0), 0)).y;
				float sampleBelow = tex3Dlod(_dyeTexture, float4(basePos + float3(0, pixelOffset, 0), 0)).y;

				float lutIndex = force / 5;// -sampleAbove;
				float3 lut = tex2Dlod(_Lut, float4(lutIndex, sampleAbove / 2, 0, 0));

				v2f o;
				float4 worldPos = mul(_Transform, float4(basePos, 1));
				o.vertex = mul(UNITY_MATRIX_P, mul(UNITY_MATRIX_V, worldPos) + float4(meshPos * scale, 0));
				o.col = lut + float3(0.1, 0.2, 0.3) * sampleBelow;
				//o.col = lut;
				o.uvs = meshPos;
				return o; 
			}
			
			fixed4 frag(v2f i) : SV_Target
			{
				float distToCenter = length(i.uvs);
				clip(.5 - distToCenter);
				return float4(i.col, 1);
			}
			ENDCG
		}
	}
}
