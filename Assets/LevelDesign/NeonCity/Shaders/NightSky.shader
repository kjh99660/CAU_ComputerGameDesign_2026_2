// Night sky over a lit city: dark overhead, a band of light pollution along the horizon, nothing below it.
Shader "REBOOT/Night Sky"
{
    Properties
    {
        [HDR] _Zenith("Overhead", Color) = (0.01, 0.014, 0.035, 1)
        [HDR] _Horizon("Horizon glow", Color) = (0.16, 0.10, 0.24, 1)
        [HDR] _Ground("Below the horizon", Color) = (0.02, 0.02, 0.035, 1)
        _GlowHeight("Glow height", Range(0.02, 1)) = 0.28
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Zenith;
            half4 _Horizon;
            half4 _Ground;
            half _GlowHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half height = normalize(input.direction).y;
                half glow = exp(-max(height, 0.0) / _GlowHeight);
                half3 sky = lerp(_Zenith.rgb, _Horizon.rgb, glow);
                half3 color = lerp(sky, _Ground.rgb, saturate(-height * 12.0));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
