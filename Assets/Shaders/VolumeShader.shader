Shader "Texture3D/VolumeShader"
{
    Properties
    {
        _Alpha("Alpha", float) = 0.02
        [NoScaleOffset]  _Gradient("Color Texture", 2D) = "" {}
    }
        SubShader
        {
            Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

            LOD 100
            Blend One OneMinusSrcAlpha
            // Cull Front
            Cull Back
            ZTest LEqual
            ZWrite Off
            Fog { Mode off }

            Pass
            {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile ___ FOCUS_PLANE_ON
                #pragma multi_compile ____ FOCUS_PLANE_HARD_CUTOFF_ON

                #include "UnityCG.cginc"

                // Maximum amount of raymarching samples
                #define MAX_STEP_COUNT 128

                // Allowed floating point inaccuracy
                #define EPSILON 0.00001f

                struct appdata
                {
                    fixed4 vertex : POSITION;
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                };

                struct v2f
                {
                    fixed4 vertex : SV_POSITION;
                    fixed3 ray_o : TEXCOORD1; // ray origin
                    fixed3 ray_d : TEXCOORD2; // ray direction
                    UNITY_VERTEX_INPUT_INSTANCE_ID
                    UNITY_VERTEX_OUTPUT_STEREO
                };

                sampler3D _MainTex;
                fixed4 _MainTex_ST;
                float _Alpha;
                sampler2D _Gradient;

                float _SliceAxis1Min, _SliceAxis1Max;
                float _SliceAxis2Min, _SliceAxis2Max;
                float _SliceAxis3Min, _SliceAxis3Max;

                v2f vert(appdata v)
                {
                    v2f o;
                    UNITY_SETUP_INSTANCE_ID(v);
                    UNITY_INITIALIZE_OUTPUT(v2f, o);
                    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                    // Vertex in object space this will be the starting point of raymarching
                    //o.objectVertex = v.vertex;

                    // calculate eye ray in object space
                    o.ray_d = -ObjSpaceViewDir(v.vertex);
                    o.ray_o = v.vertex; // v.vertex.xyz - o.ray_d;
                    o.vertex = UnityObjectToClipPos(v.vertex);

                    return o;
                }

                fixed4 BlendUnder(fixed4 color, fixed4 newColor)
                {
                    color.rgb += (1.0 - color.a) * newColor.a * newColor.rgb;
                    //color.a += (1.0 - color.a) * newColor.a;
                    return color;
                }

                // calculates intersection between a ray and a box
                // http://www.siggraph.org/education/materials/HyperGraph/raytrace/rtinter3.htm
                bool IntersectBox(float3 ray_o, float3 ray_d, float3 boxMin, float3 boxMax, out float tNear, out float tFar)
                {
                    // compute intersection of ray with all six bbox planes
                    float3 invR = 1.0 / ray_d;
                    float3 tBot = invR * (boxMin.xyz - ray_o);
                    float3 tTop = invR * (boxMax.xyz - ray_o);
                    // re-order intersections to find smallest and largest on each axis
                    float3 tMin = min(tTop, tBot);
                    float3 tMax = max(tTop, tBot);
                    // find the largest tMin and the smallest tMax
                    float2 t0 = max(tMin.xx, tMin.yz);
                    float largest_tMin = max(t0.x, t0.y);
                    t0 = min(tMax.xx, tMax.yz);
                    float smallest_tMax = min(t0.x, t0.y);
                    // check for hit
                    bool hit = (largest_tMin <= smallest_tMax);
                    tNear = largest_tMin;
                    tFar = smallest_tMax;
                    return hit;
                }

                inline float InverseLerp(float a, float b, float value)
                {
                    return saturate((value - a) / (b - a));
                }

                inline float DistanceFromPlane(float3 pos, float4 plane)
                {
                    return dot(plane.xyz, pos) + plane.w;
                }

                fixed4 DataToColor(fixed3 samplePosition)
                {
                    samplePosition = samplePosition + 0.5f;
                    float2 uv = TRANSFORM_TEX(samplePosition.xz, _MainTex);
                    float3 uvw = float3(uv.x, samplePosition.y, uv.y);
                    fixed4 sampledData = tex3D(_MainTex, uvw);

                    float data = sampledData.r;
                    float alpha = sampledData.g;

                    fixed4 sampledColor = fixed4(tex2D(_Gradient, fixed2(data, 0.5)).xyz, data == 0 ? 0 : alpha);
                    sampledColor.a *= _Alpha;

                    return sampledColor;
                }

                struct frag_out
                {
                    float4 color : SV_TARGET;
                    float depth : SV_DEPTH;
                };


                // Converts local position to depth value
                float localToDepth(float3 localPos)
                {
                    float4 clipPos = UnityObjectToClipPos(float4(localPos, 1.0f));

#if defined(SHADER_API_GLCORE) || defined(SHADER_API_OPENGL) || defined(SHADER_API_GLES) || defined(SHADER_API_GLES3)
                    return (clipPos.z / clipPos.w) * 0.5 + 0.5;
#else
                    return clipPos.z / clipPos.w;
#endif
                }

                 // frag_out frag(v2f i) // : SV_Target
                fixed4 frag(v2f i) : SV_Target
                {
                    // Start raymarching at the front surface of the object
                    fixed3 rayOrigin = i.ray_o;

                    // this needs to be normalized here and not in the vert shader, 
                    // as it's linearly interpolated, and would need to be renormalized anyway
                    fixed3 rayDirection = normalize(i.ray_d);
                    fixed3 samplePosition = rayOrigin;
                    fixed4 color = fixed4(0, 0, 0, 0);

                    float stepSize = sqrt(3) / MAX_STEP_COUNT;
                    uint iDepth = 0;
                    [unroll(MAX_STEP_COUNT)]
                    for (int i = 0; i < MAX_STEP_COUNT; i++)
                    {
                        // Accumulate color only within unit cube bounds
                        float3 absSample = abs(samplePosition.xyz);

                        if (max(absSample.x, max(absSample.y, absSample.z)) < 0.5f + EPSILON)
                        {
                            color = BlendUnder(color, DataToColor(samplePosition));
                            // color = max(color, DataToColor(samplePosition));
                            // iDepth = i;
                            samplePosition += rayDirection * stepSize;
                        }
                    }
                /* // if we want to write out distance, this is how we'd do it.
                  frag_out output;

                  output.color = color;

                  if (iDepth != 0 && color.a != 0)
                      output.depth = localToDepth(rayOrigin + rayDirection * (iDepth * stepSize) - float3(0.5f, 0.5f, 0.5f));
                  else
                      output.depth = 0;

                  return output;*/
                  return color;
              }
              ENDCG
          }
        }
}