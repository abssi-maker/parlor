// Transparent shader for sparkles, glows, contact shadows, caption panels and light beams.
// Blend factors are properties so one shader covers alpha-blend and additive.
// The alpha channel blends separately so the passthrough underlay composites correctly:
//   alpha blend: a shadow darkens the real table (alpha accumulates);
//   additive:    light is added on top of passthrough (alpha untouched).
Shader "Parlor/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Soft ("Radial soft edge (0 = use texture)", Range(0,1)) = 0
        _ClipMode ("Portal clip (0 none, 1 below table, 2 hole)", Float) = 0
        _ClipPad ("Portal clip pad (m)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcAlpha ("Src alpha", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstAlpha ("Dst alpha", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend], [_SrcAlpha] [_DstAlpha]
            Cull [_Cull]
            ZWrite Off
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ParlorPortal.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Soft;
                float _ClipMode;
                float _ClipPad;
                float _SrcBlend;
                float _DstBlend;
                float _SrcAlpha;
                float _DstAlpha;
                float _Cull;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 uvRaw : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.uvRaw = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                ParlorPortalClip(i.positionWS, GetCameraPositionWS(), _ClipMode, _ClipPad);

                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color * i.color;
                if (_Soft > 0.0)
                {
                    // procedural round falloff: no texture needed for blobs, glows and sparkles
                    half r = length(i.uvRaw * 2.0 - 1.0);
                    half f = saturate((1.0 - r) / _Soft);
                    c.a *= f * f * (3.0 - 2.0 * f);
                }
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
