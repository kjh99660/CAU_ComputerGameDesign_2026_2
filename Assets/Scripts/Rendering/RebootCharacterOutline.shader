// Constant-width outline around a character (URP). PlayerOutline uses it with two materials on the same renderers:
//   mask - width 0, no colour: marks the character's silhouette in one stencil bit,
//   hull - the mesh pushed outward by a few pixels, drawn only where that bit is not set.
// What is left of the hull is a band around the silhouette. The depth test is a material value, so the band can be
// drawn over whatever stands in front of the character or only where the character is visible.
Shader "Hidden/Reboot/CharacterOutline"
{
    Properties
    {
        _OutlineColor("Colour", Color) = (1, 1, 1, 1)
        _OutlinePixels("Width (pixels on a 1080-high screen)", Float) = 0
        [HideInInspector] _StencilComp("Stencil comparison", Float) = 8
        [HideInInspector] _StencilPass("Stencil pass", Float) = 2
        [HideInInspector] _ColorMask("Colour mask", Float) = 15
        [HideInInspector] _ZTest("Depth test", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest [_ZTest]
            ColorMask [_ColorMask]
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref 64
                ReadMask 64
                WriteMask 64
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float _OutlinePixels;
            float _StencilComp;
            float _StencilPass;
            float _ColorMask;
            float _ZTest;
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Push along the normal as seen on screen, by the same number of pixels at any distance.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 direction = mul((float3x3)UNITY_MATRIX_VP, normalWS).xy;
                direction.x *= _ScreenParams.x / _ScreenParams.y;
                float lengthSquared = dot(direction, direction);
                direction = lengthSquared > 1e-8 ? direction * rsqrt(lengthSquared) : float2(0, 0);
                float pixels = _OutlinePixels * _ScreenParams.y / 1080.0;
                positionCS.xy += direction * (pixels * 2.0) / _ScreenParams.xy * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 OutlineFragment(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
