Shader "Hidden/Chest/DepthNormalOutline"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "DepthNormalOutline"

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_DepthNormalTex);
            SAMPLER(sampler_DepthNormalTex);

            float4 _MainTex_TexelSize;
            float4 _LineColor;
            float _DepthSensitivity;
            float _NormalSensitivity;
            float _EdgeThreshold;
            float _EdgeSoftness;
            float _Thickness;
            float _FlipY;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.uv = input.uv;

                return output;
            }

            float2 GetSampleUv(float2 uv)
            {
                if (_FlipY > 0.5)
                {
                    uv.y = 1.0 - uv.y;
                }

                return uv;
            }

            float4 SampleDepthNormal(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_DepthNormalTex, sampler_DepthNormalTex, uv);
            }

            float EdgeValue(float2 uv)
            {
                float2 texel = _MainTex_TexelSize.xy * max(0.5, _Thickness);
                float4 centerSample = SampleDepthNormal(uv);
                float3 centerNormal = centerSample.rgb * 2.0 - 1.0;
                float centerDepth = centerSample.a;

                float maxDepthDelta = 0.0;
                float maxNormalDelta = 0.0;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0)
                        {
                            continue;
                        }

                        float2 sampleUv = uv + float2(x, y) * texel;
                        float4 neighborSample = SampleDepthNormal(sampleUv);
                        float3 neighborNormal = neighborSample.rgb * 2.0 - 1.0;
                        float neighborDepth = neighborSample.a;

                        maxDepthDelta = max(maxDepthDelta, abs(neighborDepth - centerDepth));
                        maxNormalDelta = max(maxNormalDelta, length(neighborNormal - centerNormal));
                    }
                }

                float depthEdge = maxDepthDelta * _DepthSensitivity;
                float normalEdge = maxNormalDelta * _NormalSensitivity;
                float edge = max(depthEdge, normalEdge);
                return smoothstep(_EdgeThreshold, _EdgeThreshold + max(0.0001, _EdgeSoftness), edge);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 sampleUv = GetSampleUv(input.uv);
                float4 sourceColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUv);
                float edge = EdgeValue(sampleUv);
                return lerp(sourceColor, _LineColor, edge * _LineColor.a);
            }
            ENDHLSL
        }
    }
}
