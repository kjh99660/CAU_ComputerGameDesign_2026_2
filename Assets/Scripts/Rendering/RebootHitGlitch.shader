// 피격 순간의 블록 깨짐 글리치(UI Lab H3) + 옅은 빨간 위험 신호 + 그 뒤에 남아 천천히 걷히는 흐림.
// RebootHitGlitchFeature가 _Amount 또는 _Blur가 0보다 클 때만 카메라 색 버퍼에 한 번 적용한다.
Shader "Hidden/Reboot/HitGlitch"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "RebootHitGlitch"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Amount;      // 0..1(최대 1.5까지 허용)
            float _Blur;        // 0..1, 피격 뒤에 남는 흐림
            float _Seed;        // 프레임마다 바뀌는 난수 시드(일시정지 중에는 고정)
            float4 _AlertColor; // 경고 빨강
            float4 _AccentColor;// 청록 포인트

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half3 SampleSplit(float2 uv, float split)
            {
                half3 c;
                c.r = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv + float2(split, 0))).r;
                c.g = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).g;
                c.b = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv - float2(split, 0))).b;
                return c;
            }

            // 황금각 나선으로 고르게 뿌린 원판 평균. radius는 화면 높이에 대한 비율이다.
            #define BLUR_TAPS 20
            half3 SampleSoft(float2 uv, float radius)
            {
                float aspect = _ScreenParams.y / _ScreenParams.x;
                half3 sum = 0;
                for (int i = 0; i < BLUR_TAPS; i++)
                {
                    float r = sqrt((i + 0.5) / BLUR_TAPS) * radius;
                    float angle = i * 2.39996323;
                    float2 offset = float2(cos(angle) * aspect, sin(angle)) * r;
                    sum += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv + offset)).rgb;
                }
                return sum / BLUR_TAPS;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float a = saturate(_Amount);

                // 블록 단위 손상: 세기에 비례한 비율의 칸만 어긋난다.
                float2 cell = floor(uv * float2(18.0, 10.0));
                float r1 = Hash(cell + _Seed);
                float r2 = Hash(cell * 1.7 + _Seed * 3.1);
                float corrupt = step(1.0 - 0.32 * a, r1) * step(0.0001, a);

                // 가로 줄 찢김
                float row = floor(uv.y * 42.0);
                float tearOn = step(1.0 - 0.16 * a, Hash(float2(row, _Seed * 7.3))) * step(0.0001, a);
                float tear = tearOn * (Hash(float2(row + 3.1, _Seed)) - 0.5) * 0.08 * a;

                float2 offset = float2(tear + corrupt * (r2 - 0.5) * 0.12 * a,
                                       corrupt * (Hash(cell + _Seed * 5.3) - 0.5) * 0.02 * a);
                float2 suv = saturate(uv + offset);
                half3 col = SampleSplit(suv, 0.004 * a + corrupt * 0.006 * a);

                // 손상 블록 색: 대부분 경고 빨강, 가끔 청록
                float tintPick = Hash(cell * 2.3 + _Seed * 11.0);
                float tintMask = corrupt * step(0.68, tintPick);
                half3 tint = tintPick > 0.93 ? _AccentColor.rgb : _AlertColor.rgb;
                col = lerp(col, col * 0.55 + tint * 0.6, tintMask * 0.6 * a);

                float2 d = uv - 0.5;
                float edge = smoothstep(0.12, 0.5, dot(d, d));

                // 맞은 뒤의 흐림: 가운데는 조금, 가장자리로 갈수록 더 흐리고 색이 살짝 빠진다.
                float blur = saturate(_Blur);
                if (blur > 0.001)
                {
                    float toEdge = smoothstep(0.0, 0.35, dot(d, d));
                    half3 soft = SampleSoft(suv, blur * lerp(0.004, 0.012, toEdge));
                    half grey = dot(soft, half3(0.299, 0.587, 0.114));
                    soft = lerp(soft, half3(grey * 1.04, grey * 0.9, grey * 0.9), 0.35 * blur);
                    col = lerp(col, soft, saturate(blur * 1.5));
                    col = lerp(col, _AlertColor.rgb, edge * 0.12 * blur);
                }

                // 화면 가장자리의 옅은 빨간 위험 신호
                col = lerp(col, _AlertColor.rgb, edge * 0.28 * a);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
