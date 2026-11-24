// Surface shader for the city stage (URP, forward and forward+).
// The city kit colours every face with one flat texel of a palette and has no normal, roughness or wear maps,
// which is what makes it read as a cartoon next to the textured characters. This shader keeps the palette colour
// and adds what a real surface has, in world space so it needs nothing from the meshes:
//   - fine surface relief (a tiling normal map projected along the three axes),
//   - wear and stains that break up colour and gloss,
//   - wet ground: darker, glossier, with puddles that mirror the lights.
// Lighting is URP's own physically based lighting (UniversalFragmentPBR).
Shader "REBOOT/City Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Colour (the kit's palette)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)
        _Saturation("Colour saturation", Range(0, 1)) = 0.75
        _Brightness("Brightness", Range(0, 2)) = 1
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Gloss where clean", Range(0, 1)) = 0.5
        _SmoothnessWorn("Gloss where worn", Range(0, 1)) = 0.2
        [NoScaleOffset][Normal] _DetailNormal("Surface relief (tiles in world space)", 2D) = "bump" {}
        _DetailScale("Relief tiles per metre", Float) = 0.5
        _DetailStrength("Relief strength", Range(0, 2)) = 0.6
        [NoScaleOffset] _GrimeMap("Wear (R), stains (G), puddle shapes (B)", 2D) = "white" {}
        _GrimeScale("Wear tiles per metre", Float) = 0.12
        _GrimeStrength("Wear on colour", Range(0, 1)) = 0.4
        _Wetness("Wet ground", Range(0, 1)) = 0
        _PuddleScale("Puddle tiles per metre", Float) = 0.05
        _PuddleCover("Puddle cover", Range(0, 1)) = 0.4
        [HDR] _EmissionColor("Glow", Color) = (0, 0, 0, 1)
        [NoScaleOffset] _EmissionMap("Glow map", 2D) = "white" {}
        [HideInInspector] _Cutoff("Alpha cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

        // Every material value is in this one buffer and nothing is left out per variant: that is what lets the
        // SRP Batcher draw the whole city without a state change per object.
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseMap_TexelSize;
        half4 _BaseColor;
        half4 _EmissionColor;
        half _Cutoff;
        half _Saturation;
        half _Brightness;
        half _Metallic;
        half _Smoothness;
        half _SmoothnessWorn;
        half _DetailStrength;
        half _GrimeStrength;
        half _Wetness;
        half _PuddleCover;
        float _DetailScale;
        float _GrimeScale;
        float _PuddleScale;
        CBUFFER_END

        TEXTURE2D(_DetailNormal);
        SAMPLER(sampler_DetailNormal);
        TEXTURE2D(_GrimeMap);
        SAMPLER(sampler_GrimeMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CityVertex
            #pragma fragment CityFragment

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings CityVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                half fogFactor = 0;
                #if !defined(_FOG_FRAGMENT)
                    fogFactor = ComputeFogFactor(position.positionCS.z);
                #endif
                output.fogFactorAndVertexLight = half4(fogFactor, VertexLighting(position.positionWS, output.normalWS));
                return output;
            }

            void CityFragment(
                Varyings input
                , out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 positionWS = input.positionWS;
                half3 faceNormal = normalize(input.normalWS);
                // How much each of the three world projections counts on this face.
                half3 weights = abs(faceNormal);
                weights *= weights;
                weights *= weights;
                weights /= (weights.x + weights.y + weights.z);

                // Wear and stains.
                float3 grimeUV = positionWS * _GrimeScale;
                half2 grime =
                    SAMPLE_TEXTURE2D(_GrimeMap, sampler_GrimeMap, grimeUV.zy).rg * weights.x +
                    SAMPLE_TEXTURE2D(_GrimeMap, sampler_GrimeMap, grimeUV.xz).rg * weights.y +
                    SAMPLE_TEXTURE2D(_GrimeMap, sampler_GrimeMap, grimeUV.xy).rg * weights.z;
                half clean = grime.r;      // 0 worn .. 1 clean, fine grain
                half stain = grime.g;      // broad patches

                // Colour: the palette, less saturated than a toy and uneven like a used surface.
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                half grey = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                albedo = lerp(grey.xxx, albedo, _Saturation) * _Brightness;
                albedo *= lerp(1.0, lerp(0.62, 1.08, clean) * lerp(0.72, 1.06, stain), _GrimeStrength);
                half smoothness = lerp(_SmoothnessWorn, _Smoothness, clean * lerp(0.6, 1.0, stain));

                // Surface relief: each projection bends the face normal a little.
                float3 detailUV = positionWS * _DetailScale;
                half3 reliefX = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, detailUV.zy), _DetailStrength);
                half3 reliefY = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, detailUV.xz), _DetailStrength);
                half3 reliefZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_DetailNormal, sampler_DetailNormal, detailUV.xy), _DetailStrength);
                reliefX = half3(reliefX.xy + faceNormal.zy, faceNormal.x);
                reliefY = half3(reliefY.xy + faceNormal.xz, faceNormal.y);
                reliefZ = half3(reliefZ.xy + faceNormal.xy, faceNormal.z);
                half3 normalWS = normalize(reliefX.zyx * weights.x + reliefY.xzy * weights.y + reliefZ.xyz * weights.z);

                // Wet ground: only what faces up. Damp everywhere, standing water where the puddle map says so.
                half faceUp = saturate((faceNormal.y - 0.9) * 12.0) * _Wetness;
                half puddleShape = SAMPLE_TEXTURE2D(_GrimeMap, sampler_GrimeMap, positionWS.xz * _PuddleScale).b;
                half puddle = smoothstep(1.0 - _PuddleCover, 1.0 - _PuddleCover + 0.1, puddleShape) * faceUp;
                albedo *= lerp(1.0, 0.6, max(puddle, faceUp * 0.35));
                smoothness = lerp(lerp(smoothness, max(smoothness, 0.42), faceUp), 0.95, puddle);
                normalWS = normalize(lerp(normalWS, faceNormal, puddle * 0.92));

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                // No keyword for glow: black by default, so a material cannot lose it by accident.
                surface.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
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

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // Normals for screen-space ambient occlusion. The flat face normal is enough for that.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
