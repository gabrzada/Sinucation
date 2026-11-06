// Contorno a tinta em ecrã (InkOutline.cs): arestas de profundidade (silhuetas) e de normais (vincos),
// com a espessura em píxeis proporcional à resolução e a tinta a desvanecer ao longe.
Shader "Hidden/FriendSlop/InkOutline"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "InkOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _FSInk; // x espessura (px a 1080p), y opacidade, z limiar de profundidade, w limiar de normais

            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv);

                float2 texel = _CameraDepthTexture_TexelSize.xy;
                float width = _FSInk.x * max(_CameraDepthTexture_TexelSize.w / 1080.0, 0.85);
                float2 o = texel * (0.5 * width);
                // Cruz de Roberts (diagonais) à volta do píxel.
                float2 uv1 = uv + float2(-o.x, -o.y), uv2 = uv + float2(o.x, -o.y);
                float2 uv3 = uv + float2(-o.x, o.y),  uv4 = uv + float2(o.x, o.y);

                float d1 = EyeDepth(uv1), d2 = EyeDepth(uv2), d3 = EyeDepth(uv3), d4 = EyeDepth(uv4);
                float dmin = max(min(min(d1, d2), min(d3, d4)), 0.01);
                float depthDelta = sqrt((d1 - d4) * (d1 - d4) + (d2 - d3) * (d2 - d3)) / dmin;

                float3 n1 = SampleSceneNormals(uv1), n2 = SampleSceneNormals(uv2);
                float3 n3 = SampleSceneNormals(uv3), n4 = SampleSceneNormals(uv4);
                float normalDelta = sqrt(dot(n1 - n4, n1 - n4) + dot(n2 - n3, n2 - n3));

                // Superfícies vistas de raspão mudam muito de profundidade entre píxeis: sobe o limiar para não
                // riscar o chão e as paredes ao longe.
                float rawCenter = SampleSceneDepth(uv);
                float3 positionWS = ComputeWorldSpacePosition(uv, rawCenter, UNITY_MATRIX_I_VP);
                float3 viewWS = normalize(_WorldSpaceCameraPos - positionWS);
                float facing = saturate(dot(SampleSceneNormals(uv), viewWS));
                float depthThreshold = _FSInk.z * (1.0 + 5.0 * (1.0 - facing) * (1.0 - facing));

                float edge = max(smoothstep(depthThreshold, depthThreshold * 1.8, depthDelta),
                                 smoothstep(_FSInk.w, _FSInk.w + 0.3, normalDelta));
                edge *= 1.0 - smoothstep(14.0, 32.0, dmin);

                half3 ink = color.rgb * 0.05h;
                color.rgb = lerp(color.rgb, ink, saturate(edge * _FSInk.y));
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
