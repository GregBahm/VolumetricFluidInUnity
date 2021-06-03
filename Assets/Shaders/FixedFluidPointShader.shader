Shader "FixedFluidPointShader"
{
	Properties
	{
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

			#define Resolution 96
			
			#include "UnityCG.cginc"

			struct MeshData
			{
				float3 pos: Position;
				float3 norm : NORMAL;
			};

			StructuredBuffer<float3> _ParticleBuffer;
			StructuredBuffer<MeshData> _MeshBuffer;

			sampler3D _fluidTexture;

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float3 col : TEXCOORD0;
			};

			v2f vert(uint id : SV_VertexID, uint inst : SV_InstanceID)
			{

				float3 basePos = _ParticleBuffer[inst];
				MeshData meshData = _MeshBuffer[id];

				float fluid = tex3Dlod(_fluidTexture, float4(basePos, 0)).y;
				float scale = saturate(fluid) * ((float)1 / Resolution);

				float4 vertPos = float4(basePos + meshData.pos * scale, 1);

				v2f o;
				o.vertex = UnityObjectToClipPos(vertPos);
				o.col = basePos;
				return o; 
			}
			
			fixed4 frag (v2f i) : SV_Target
			{
				return float4(i.col, 1);
			}
			ENDCG
		}
	}
}
