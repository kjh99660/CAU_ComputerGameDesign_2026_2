// A hologram copy of the player, left along the dash of the skill: see-through light in the colour of the HUD, with
// bright edges and scan lines climbing through it. It builds up from the feet (_Reveal 0..1, a bright band at the
// front) and breaks up into horizontal slices that jitter sideways as it goes (_Dissolve 0..1).
// Additive and without depth writing, so it reads as light and never hides what is behind it.
// _Clock is the effect's own clock in seconds: the copy holds still while the game is paused.
Shader "REBOOT/Skill Ghost"
{
    Properties
    {
        [HDR] _Color("Body colour", Color) = (0.25, 1.7, 2.0, 1)
        [HDR] _EdgeColor("Edge colour", Color) = (1.6, 2.2, 2.4, 1)
        _Fade("Fade", Range(0, 1)) = 1
        _Reveal("Built up from the feet (0-1)", Range(0, 1)) = 1
        _Dissolve("Broken up (0-1)", Range(0, 1)) = 0
        _BaseY("World height of the feet", Float) = 0
        _Height("Height in metres", Float) = 1.9
        _ScanDensity("Scan lines per metre", Float) = 26
        _Clock("Clock in seconds", Float) = 0
        _Seed("Seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Ghost"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _EdgeColor;
            half _Fade;
            half _Reveal;
            half _Dissolve;
            float _BaseY;
            float _Height;
            float _ScanDensity;
            float _Clock;
            float _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            float Hash(float value)
            {
                return frac(sin(value * 12.9898 + _Seed * 3.17) * 43758.5453);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // As it breaks up, each horizontal slice slips sideways on the screen.
                float slice = floor(positionWS.y * 14.0);
                float slip = (Hash(slice + 31.0) - 0.5) * 0.35 * _Dissolve * _Dissolve;
                positionWS += normalize(UNITY_MATRIX_V[0].xyz) * slip;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float height = (input.positionWS.y - _BaseY) / max(_Height, 0.01);
                // Built up from the feet: everything below the front is there, the front itself is a bright band.
                float front = _Reveal * 1.2 - 0.05;
                half built = step(height, front);
                half band = saturate(1.0 - abs(height - front) / 0.07) * step(_Reveal, 0.995);
                // Scan lines climbing through the body.
                half scan = 0.55 + 0.45 * step(0.5, frac(input.positionWS.y * _ScanDensity - _Clock * 2.5));
                // Slices dropping out as it goes.
                half kept = step(_Dissolve, Hash(floor(input.positionWS.y * 14.0)) * 0.998 + 0.001);
                half flicker = 0.84 + 0.16 * sin(_Clock * 61.0 + _Seed * 7.0);
                half rim = 1.0 - saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                rim *= rim;
                // Kept dim inside and bright at the edges: seen along the dash the copies overlap, and they add up.
                half3 light = _Color.rgb * (0.1 + 0.3 * scan) + _EdgeColor.rgb * (rim * 1.0 + band * 1.4);
                return half4(light * (_Fade * built * kept * flicker), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
