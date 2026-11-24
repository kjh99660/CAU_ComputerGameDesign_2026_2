// What is left of a destroyed enemy (its body lying on the floor, or the pieces it broke into) being eaten away:
// bites open at scattered points and grow until nothing is left, with small irregular nibbles along each rim, a
// charred band behind the rim and a thin glowing edge. Same lighting as REBOOT/Enemy Lit so a body looks the same
// the moment it dies. _Dissolve (0 = whole, 1 = gone) and _Seed are set per body by the game. Every pass, the
// shadow and depth passes included, cuts the same holes, so nothing eaten still casts a shadow.
// What stays lit is set per body and part by the game as well (EnemyRemainsRuntime / RemainsLight): the glowing parts
// and rim in _GlowHue scaled by 1 - _LightsOut, and while _Spark is up, electric arcs: thin bright lines where a noise
// field crosses its middle value, a new set for every flash (_Seed.w).
Shader "REBOOT/Enemy Remains"
{
    Properties
    {
        [MainTexture] _BaseMap("Colour", 2D) = "white" {}
        [MainColor] _BaseColor("Tint (copied from the enemy)", Color) = (1, 1, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Gloss", Range(0, 1)) = 0.5
        [NoScaleOffset] _GlowMap("Glowing parts (R)", 2D) = "black" {}
        [HDR] _GlowColor("Glow colour (multiplied by the tint's hue)", Color) = (1, 1, 1, 1)
        _GlowStrength("Glow of the marked parts", Range(0, 16)) = 6
        _RimStrength("Rim light", Range(0, 8)) = 1.2
        _RimPower("Rim width (higher is thinner)", Range(0.5, 8)) = 3
        _Dissolve("Eaten (0 whole, 1 gone)", Range(0, 1)) = 0
        _BiteScale("Bites per metre", Range(1, 30)) = 7
        _EdgeWidth("Glowing edge width", Range(0.005, 0.3)) = 0.05
        [HDR] _EdgeColor("Glowing edge colour", Color) = (4, 1.1, 0.25, 1)
        _CharWidth("Charred band width", Range(0.01, 0.5)) = 0.16
        _Seed("Pattern offset (set per body)", Vector) = (0, 0, 0, 0)
        _LightsOut("Lights fading out (0 on, 1 off)", Range(0, 1)) = 0
        _GlowHue("Colour of its lights (the hue counts; grey gives none)", Color) = (1, 0.35, 0.15, 1)
        _Spark("Electric arcs now (set by the game)", Range(0, 1)) = 0
        [HDR] _SparkColor("Electric arc colour", Color) = (0.55, 1.35, 4, 1)
        _SparkScale("Electric arcs per metre", Range(1, 40)) = 7
        [HideInInspector] _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseMap_TexelSize;
        half4 _BaseColor;
        half4 _GlowColor;
        half4 _EdgeColor;
        half4 _GlowHue;
        half4 _SparkColor;
        float4 _Seed;
        half _Cutoff;
        half _Metallic;
        half _Smoothness;
        half _GlowStrength;
        half _RimStrength;
        half _RimPower;
        half _Dissolve;
        half _BiteScale;
        half _EdgeWidth;
        half _CharWidth;
        half _LightsOut;
        half _Spark;
        half _SparkScale;
        CBUFFER_END

        TEXTURE2D(_GlowMap);
        SAMPLER(sampler_GlowMap);

        float3 RemainsHash3(float3 p)
        {
            p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
            return frac(sin(p) * 43758.5453);
        }

        float RemainsHash1(float3 p)
        {
            return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
        }

        float RemainsValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float3 u = f * f * (3.0 - 2.0 * f);
            float a = lerp(RemainsHash1(i), RemainsHash1(i + float3(1, 0, 0)), u.x);
            float b = lerp(RemainsHash1(i + float3(0, 1, 0)), RemainsHash1(i + float3(1, 1, 0)), u.x);
            float c = lerp(RemainsHash1(i + float3(0, 0, 1)), RemainsHash1(i + float3(1, 0, 1)), u.x);
            float d = lerp(RemainsHash1(i + float3(0, 1, 1)), RemainsHash1(i + float3(1, 1, 1)), u.x);
            return lerp(lerp(a, b, u.y), lerp(c, d, u.y), u.z);
        }

        // Distance to the nearest of a set of scattered points: 0 at a bite's centre.
        float RemainsCells(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            float nearest = 8.0;
            [unroll] for (int z = -1; z <= 1; z++)
            [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int x = -1; x <= 1; x++)
            {
                float3 g = float3(x, y, z);
                float3 r = g + RemainsHash3(i + g) - f;
                nearest = min(nearest, dot(r, r));
            }
            return sqrt(nearest);
        }

        // Signed distance to the eaten front: below zero the surface is gone.
        float RemainsFront(float3 positionOS)
        {
            float3 p = positionOS * _BiteScale + _Seed.xyz;
            float bites = RemainsCells(p);
            float nibbles = RemainsValueNoise(p * 3.7) * 0.22 + RemainsValueNoise(p * 9.1) * 0.08;
            return bites + nibbles - _Dissolve * 1.25;
        }

        // Electric arcs: a bright core and a faint halo along the line where two octaves of noise cross their middle
        // value, only in a few patches (a short circuit, not the whole surface). The flash number (_Seed.w) moves
        // both, so every flash draws new arcs in new places.
        half RemainsArcs(float3 positionOS)
        {
            float3 shift = _Seed.w * float3(3.17, 1.93, 2.71);
            float3 p = positionOS * _SparkScale + _Seed.xyz + shift;
            float d = abs(RemainsValueNoise(p) + RemainsValueNoise(p * 2.3) * 0.35 - 0.675);
            half core = saturate(1.0 - d * 22.0);
            half halo = saturate(1.0 - d * 7.0);
            half patch = smoothstep(0.5, 0.62, RemainsValueNoise(positionOS * 2.5 + _Seed.zxy + shift * 0.41));
            return (core * core * core + halo * halo * 0.08) * patch;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RemainsVertex
            #pragma fragment RemainsFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ REFLECTION_PROBE_ROTATION
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 fogFactorAndVertexLight : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings RemainsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                half fogFactor = 0;
                #if !defined(_FOG_FRAGMENT)
                    fogFactor = ComputeFogFactor(position.positionCS.z);
                #endif
                output.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(position.positionWS, output.normalWS));
                return output;
            }

            void RemainsFragment(
                Varyings input
                , bool frontFace : SV_IsFrontFace
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float front = RemainsFront(input.positionOS);
                clip(front);
                half edge = saturate(1.0 - front / max(_EdgeWidth, 0.0001));
                half charred = saturate(1.0 - front / max(_CharWidth, 0.0001)) * step(0.0001, _Dissolve);

                float3 positionWS = input.positionWS;
                // Through a hole the inside faces show; light them as if they faced the viewer.
                half3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(positionWS);

                half peak = max(max(_GlowHue.r, _GlowHue.g), max(_GlowHue.b, 0.001));
                half low = min(min(_GlowHue.r, _GlowHue.g), _GlowHue.b);
                half3 hue = _GlowHue.rgb / peak;
                hue *= hue;
                half lit = saturate(((peak - low) / peak - 0.25) * 2.5) * (1.0 - _LightsOut);

                half mask = SAMPLE_TEXTURE2D(_GlowMap, sampler_GlowMap, input.uv).r;
                half rim = pow(1.0 - saturate(dot(normalWS, viewWS)), _RimPower);
                half arcs = 0;
                UNITY_BRANCH if (_Spark > 0.001)
                    arcs = RemainsArcs(input.positionOS) * _Spark;

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb * (1.0 - 0.85 * charred);
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness * (1.0 - charred);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                surface.emission = hue * _GlowColor.rgb * (mask * _GlowStrength + rim * _RimStrength) * lit
                                   + _EdgeColor.rgb * edge * edge * step(0.0001, _Dissolve)
                                   + _SparkColor.rgb * arcs;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                #endif
                inputData.fogCoord = InitializeInputDataFog(float4(positionWS, 1.0), input.fogFactorAndVertexLight.x);
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                outColor = half4(color.rgb, 1);

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RemainsShadowVertex
            #pragma fragment RemainsShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings RemainsShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 RemainsShadowFragment(ShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(RemainsFront(input.positionOS));
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RemainsDepthVertex
            #pragma fragment RemainsDepthFragment
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings RemainsDepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half RemainsDepthFragment(DepthVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(RemainsFront(input.positionOS));
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RemainsNormalsVertex
            #pragma fragment RemainsNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            struct NormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            NormalsVaryings RemainsNormalsVertex(NormalsAttributes input)
            {
                NormalsVaryings output = (NormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void RemainsNormalsFragment(
                NormalsVaryings input
                , bool frontFace : SV_IsFrontFace
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                clip(RemainsFront(input.positionOS));
                float3 normalWS = normalize(input.normalWS) * (frontFace ? 1.0 : -1.0);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    outNormalWS = half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                    outNormalWS = half4(normalWS, 0.0);
                #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
