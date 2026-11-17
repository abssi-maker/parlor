// Hand-mesh occlusion. Draws the tracked hand mesh as depth plus a transparent-black colour, which punches
// the virtual layer away wherever the hand is nearer than virtual content. The passthrough underlay then
// shows the user's real hand there, so real hands correctly cover the magician and the props.
// This is the standard passthrough "punch-through" pattern; it does not depend on the Depth API.
// _Ghost > 0 adds a faint visible tint, used in the simulator where there is no real hand to see.
Shader "Parlor/HandOccluder"
{
    Properties
    {
        _Ghost ("Visible ghost tint (0 = pure occluder)", Color) = (0,0,0,0)
    }
    SubShader
    {
        // Just before other opaques so later virtual geometry behind the hand is rejected by the depth test.
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend Off
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Ghost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fresnel : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                half3 n = TransformObjectToWorldNormal(v.normalOS);
                half3 view = normalize(GetCameraPositionWS() - ws);
                o.fresnel = 1.0 - saturate(dot(n, view));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Premultiplied: rgb and alpha both scale with the ghost amount; (0,0,0,0) is a pure hole.
                // Simulator only (_Ghost.a = 0 on a headset): mostly opaque with a darker silhouette edge, so the
                // hands read clearly even over a white tabletop in the demo footage.
                half a = _Ghost.a * (0.80 + 0.20 * i.fresnel);
                half3 c = _Ghost.rgb * (1.0 - 0.45 * i.fresnel);
                return half4(c * a, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
