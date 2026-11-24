// Neon markings projected on the floor, in the look of the game's HUD (cyan, turning hot magenta).
//   Area mode: the charge attack's reach. A bright double rim and side edges, tick marks inside the rim, range rings
//   every metre, a faint floor grid, a scan line pulsing outward, and the floor filling from the player out as the
//   charge builds (_Charge 0..1), with a bright front where the fill has reached. Fully charged, it flickers.
//   The mesh gives uv.x = across the area (0..1) and uv.y = from the player (0) to the rim (1).
//   Line mode: the special's shock ring (a LineRenderer): a hot core, a soft glow and dashes running round it.
// Additive, so it reads as light on the floor and never hides what is under it.
Shader "REBOOT/Ground Indicator"
{
    Properties
    {
        [HDR] _Color("Neon colour", Color) = (0.25, 1.7, 2.0, 1)
        [HDR] _HotColor("Hot colour (full charge)", Color) = (2.4, 0.35, 1.1, 1)
        _Charge("Charge (0-1)", Range(0, 1)) = 0
        _Fade("Fade", Range(0, 1)) = 1
        [Enum(Area, 0, Line, 1)] _Mode("Mode", Float) = 0
        [Enum(Sector, 0, Box, 1)] _Shape("Area shape", Float) = 0
        _Arc("Sector arc (degrees)", Float) = 270
        _Size("Area size in metres (x: radius or width, y: length)", Vector) = (3, 3, 0, 0)
        _Dashes("Dashes round a line", Float) = 48
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Indicator"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _HotColor;
            float4 _Size;
            half _Charge;
            half _Fade;
            half _Mode;
            half _Shape;
            float _Arc;
            float _Dashes;
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
                half4 color : COLOR;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            // 1 on the line, falling to 0 at "width" away from it.
            half Band(float distance, float width)
            {
                return saturate(1.0 - abs(distance) / max(width, 1e-4));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                half3 neon = lerp(_Color.rgb, _HotColor.rgb, saturate(_Charge));

                if (_Mode > 0.5)
                {
                    float across = input.uv.y - 0.5;
                    half core = Band(across, 0.16);
                    half glow = Band(across, 0.5);
                    half dash = step(frac(input.uv.x * _Dashes - t * 1.5), 0.72);
                    half shimmer = 0.75 + 0.25 * sin(input.uv.x * 120.0 - t * 25.0);
                    half stroke = (core * 1.4 + glow * glow * 0.5) * lerp(0.35, 1.0, dash) * shimmer;
                    return half4(neon * stroke * input.color.a * _Fade, 1);
                }

                float radial = input.uv.y;
                bool sector = _Shape < 0.5;
                float extent = sector ? _Size.x : _Size.y;
                float metres = radial * extent;
                float rimMetres = (1.0 - radial) * extent;
                float sideMetres;
                float tickCoordinate;
                if (sector)
                {
                    sideMetres = _Arc < 359.0 ? min(input.uv.x, 1.0 - input.uv.x) * radians(_Arc) * metres : 1e3;
                    tickCoordinate = input.uv.x * _Arc / 10.0;          // a tick every 10 degrees
                }
                else
                {
                    sideMetres = min(input.uv.x, 1.0 - input.uv.x) * _Size.x;
                    tickCoordinate = input.uv.x * _Size.x * 2.0;        // a tick every half metre
                }

                half rim = Band(rimMetres, 0.07) + 0.45 * Band(rimMetres - 0.22, 0.03);
                half sides = Band(sideMetres, 0.06);
                half ticks = step(frac(tickCoordinate), 0.18) * Band(rimMetres - 0.38, 0.1);
                float2 cell = abs(frac(input.positionWS.xz * 2.0) - 0.5);
                half grid = 0.18 * Band(0.5 - max(cell.x, cell.y), 0.035);
                half rings = 0.22 * Band(frac(metres + 0.5) - 0.5, 0.025);
                half filled = step(radial, _Charge);
                half front = Band(radial - _Charge, 0.02) * step(_Charge, 0.995);
                half scan = 0.5 * Band(frac(t * 0.9) - radial, 0.05);
                half fill = 0.06 + 0.1 * radial * radial + 0.12 * filled;

                half light = rim * 1.3 + sides * 0.9 + ticks * 0.8 + grid + rings + front * 1.1 + scan + fill;
                light *= _Charge >= 0.995 ? 0.85 + 0.15 * sin(t * 55.0) : 1.0;
                light *= smoothstep(0.03, 0.15, radial);   // keep the floor right under the player clear
                return half4(neon * light * _Fade, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
