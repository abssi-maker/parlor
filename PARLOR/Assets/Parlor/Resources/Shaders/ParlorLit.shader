// Opaque surface shader for everything solid in PARLOR.
// Quest-safe on purpose: one forward pass, no realtime lights, no shadows, no keywords, no derivatives.
// Lighting is faked: half-lambert against one global key direction, a warm rim, and a pit-depth glow.
// Always writes alpha = 1 so the passthrough underlay is hidden exactly where virtual things are.
Shader "Parlor/Lit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Shade ("Shadow tint", Color) = (0.32,0.20,0.24,1)
        _Unlit ("Unlit amount", Range(0,1)) = 0
        _Rim ("Rim strength", Range(0,2)) = 0.25
        _PitGlow ("Pit depth glow", Range(0,1)) = 0
        _Cutoff ("Alpha cutoff (0 = off)", Range(0,1)) = 0
        _ClipMode ("Portal clip (0 none, 1 below table, 2 hole)", Float) = 0
        _ClipPad ("Portal clip pad (m)", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

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
                half4 _Shade;
                half _Unlit;
                half _Rim;
                half _PitGlow;
                half _Cutoff;
                float _ClipMode;
                float _ClipPad;
                float _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
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
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 cam = GetCameraPositionWS();
                ParlorPortalClip(i.positionWS, cam, _ClipMode, _ClipPad);

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half alpha = tex.a * _Color.a * i.color.a;
                if (_Cutoff > 0.0) clip(alpha - _Cutoff);

                half3 albedo = tex.rgb * _Color.rgb * i.color.rgb;
                half3 n = normalize(i.normalWS) * IS_FRONT_VFACE(facing, 1.0, -1.0);
                half3 v = normalize(cam - i.positionWS);

                half ndl = dot(n, normalize(_ParlorKeyDir.xyz + half3(0.0, 0.0001, 0.0))) * 0.5 + 0.5;
                half3 lit = albedo * lerp(_Shade.rgb, half3(1.06, 1.02, 0.96), ndl * ndl);
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _Rim;
                lit += rim * _ParlorWarm.rgb * (0.35 + albedo);

                half depth = ParlorPitDepth(i.positionWS);
                half glow = lerp(1.0, lerp(0.42, 1.30, depth * depth) , _PitGlow);
                half3 warm = lerp(half3(1, 1, 1), lerp(half3(0.80, 0.70, 0.78), _ParlorWarm.rgb * 1.25, depth), _PitGlow);
                lit *= glow * warm;

                half3 col = lerp(lit, albedo, _Unlit);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
