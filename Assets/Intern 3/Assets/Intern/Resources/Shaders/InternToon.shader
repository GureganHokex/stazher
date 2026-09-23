// Мультяшный шейдер для URP: ступенчатый свет, цветные тени, блик по краю и обводка.
Shader "Intern/Toon"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1,1,1,1)
        _ShadeColor ("Shade Tint", Color) = (0.62,0.6,0.88,1)
        _RampThreshold ("Ramp Threshold", Range(-1,1)) = 0.05
        _RampSmooth ("Ramp Smoothness", Range(0.001,0.5)) = 0.05
        _RimColor ("Rim (A = strength)", Color) = (1,1,1,0.28)
        _EmissionColor ("Emission", Color) = (0,0,0,0)
        _OutlineColor ("Outline Color", Color) = (0.13,0.1,0.25,1)
        _OutlineWidth ("Outline Width", Float) = 0.0035
        _GroundAO ("Ground AO", Range(0,1)) = 0.22
        _ToonSpec ("Toon Spec (A = strength)", Color) = (1,1,1,0.25)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half4 _RimColor;
            half4 _EmissionColor;
            half4 _OutlineColor;
            half4 _ToonSpec;
            half _GroundAO;
            half _RampThreshold;
            half _RampSmooth;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fog        : TEXCOORD2;
            };

            Varyings vert (Attributes i)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = dot(n, L.direction);
                half lit = smoothstep(_RampThreshold - _RampSmooth, _RampThreshold + _RampSmooth, ndl);
                lit *= smoothstep(0.3, 0.7, L.shadowAttenuation);

                half3 baseCol = _BaseColor.rgb;
                half3 shade = baseCol * _ShadeColor.rgb;
                half3 col = lerp(shade, baseCol * saturate(L.color), lit);
                col += baseCol * SampleSH(n) * 0.35;

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                half rim = pow(1.0 - saturate(dot(n, v)), 4.0) * _RimColor.a;
                col += rim * _RimColor.rgb * (0.4 + 0.6 * lit);
                // блик-«пятнышко» как в мультфильмах
                half3 hv = normalize(L.direction + v);
                half sp = smoothstep(0.965, 0.98, dot(n, hv)) * lit * _ToonSpec.a;
                col += sp * _ToonSpec.rgb;
                // мягкое затемнение у пола — фальшивый ambient occlusion
                col *= lerp(1.0 - _GroundAO, 1.0, saturate(i.positionWS.y * 1.6));
                col += _EmissionColor.rgb;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                float4 cs = TransformObjectToHClip(i.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(i.normalOS);
                float2 nCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                float len = max(length(nCS), 1e-5);
                float2 off = nCS / len * _OutlineWidth * cs.w * 2.0;
                off.x *= _ScreenParams.y / _ScreenParams.x;
                cs.xy += off;
                o.positionCS = cs;
                return o;
            }

            half4 frag (Varyings i) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
