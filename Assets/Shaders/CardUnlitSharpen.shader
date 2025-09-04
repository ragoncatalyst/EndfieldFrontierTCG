Shader "Endfield/CardUnlitSharpen"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _SharpenStrength ("Sharpen Strength", Range(0,1)) = 0.2
        _MipBias ("Mip Bias", Range(-2,2)) = -0.8
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Tags { "LightMode"="Always" }
            Cull Back ZWrite On ZTest LEqual
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize; // x=1/width, y=1/height
            fixed4 _Color;
            float _SharpenStrength;
            float _MipBias;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 SampleBias(float2 uv, float bias)
            {
                #if defined(SHADER_API_GLES)
                    return tex2D(_MainTex, uv);
                #else
                    return tex2Dbias(_MainTex, float4(uv, 0, bias));
                #endif
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 texel = _MainTex_TexelSize.xy;
                fixed4 c = SampleBias(i.uv, _MipBias);

                // 5-tap unsharp mask (cross) to提升细节但避免过锐
                fixed4 n = SampleBias(i.uv + float2(0, -texel.y), _MipBias);
                fixed4 s = SampleBias(i.uv + float2(0,  texel.y), _MipBias);
                fixed4 e = SampleBias(i.uv + float2( texel.x, 0), _MipBias);
                fixed4 w = SampleBias(i.uv + float2(-texel.x, 0), _MipBias);

                fixed4 blur = (n + s + e + w + c) * 0.2;
                fixed4 sharpened = c + (c - blur) * _SharpenStrength;

                return saturate(sharpened * _Color);
            }
            ENDCG
        }
    }
}


