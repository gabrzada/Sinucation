// Fantasma (jogadores eliminados): translúcido, com contorno brilhante (fresnel) e luz suave de cima.
// O primeiro passe só escreve profundidade, para o boneco ficar translúcido "por fora" sem se ver o interior
// das peças umas através das outras. Não depende das variantes transparentes do URP Lit (removidas na build).
Shader "FriendSlop/Ghost"
{
    Properties
    {
        _Color ("Tint", Color) = (0.8, 0.9, 1, 0.38)
        _RimColor ("Rim", Color) = (0.65, 0.95, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            ColorMask 0
            Cull Back
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _RimColor;
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
                float light = 0.6 + 0.4 * saturate(dot(n, normalize(float3(0.25, 0.9, 0.2))));
                fixed4 c;
                c.rgb = _Color.rgb * light + _RimColor.rgb * rim * 0.9;
                c.a = saturate(_Color.a + rim * 0.55);
                return c;
            }
            ENDCG
        }
    }
}
