// Vidro simples e fiável para globos e vitrines: centro quase invisível, borda brilhante (fresnel) e um
// brilho especular falso. Não depende das variantes transparentes do URP Lit (que ficavam leitosas).
Shader "FriendSlop/Glass"
{
    Properties
    {
        _Color ("Tint", Color) = (0.85, 0.93, 1, 1)
        _CenterAlpha ("Center Alpha", Range(0, 1)) = 0.04
        _RimAlpha ("Rim Alpha", Range(0, 1)) = 0.55
        _RimPower ("Rim Power", Range(0.5, 8)) = 3
        // Surface Cache GI reads RGB transmission separately from the visible Fresnel pass.
        [HideInInspector] _TransparencyLM ("GI transmission", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float _CenterAlpha;
            float _RimAlpha;
            float _RimPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = WorldSpaceViewDir(v.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.normal);
                float3 v = normalize(i.viewDir);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                // Brilho especular falso de uma luz vinda de cima e da esquerda.
                float3 h = normalize(normalize(float3(-0.4, 0.8, 0.3)) + v);
                float spec = pow(saturate(dot(n, h)), 90.0);
                fixed4 c = _Color;
                c.rgb = lerp(c.rgb, 1.0, rim * 0.5) + spec;
                c.a = saturate(lerp(_CenterAlpha, _RimAlpha, rim) + spec);
                return c;
            }
            ENDCG
        }
        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off
            Blend One Zero
            ZWrite Off
            CGPROGRAM
            #pragma vertex MetaVertex
            #pragma fragment MetaFragment
            #include "UnityCG.cginc"
            #include "UnityMetaPass.cginc"
            float4 _Color;
            float _CenterAlpha;
            struct MetaAttributes { float4 vertex : POSITION; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; };
            struct MetaVaryings { float4 position : SV_POSITION; };
            MetaVaryings MetaVertex(MetaAttributes v)
            {
                MetaVaryings o;
                o.position = UnityMetaVertexPosition(v.vertex, v.uv1, v.uv2, unity_LightmapST, unity_DynamicLightmapST);
                return o;
            }
            float4 MetaFragment(MetaVaryings i) : SV_Target
            {
                UnityMetaInput surface;
                UNITY_INITIALIZE_OUTPUT(UnityMetaInput, surface);
                surface.Albedo = _Color.rgb * _CenterAlpha;
                surface.Emission = 0;
                return UnityMetaFragment(surface);
            }
            ENDCG
        }
    }
}
