// Эффекты: солнечные лучи, пылинки (аддитивно) и мягкие тени-пятна (полупрозрачно).
Shader "Intern/Fx"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,0.3)
        _Radial ("Radial (1) / Beam (0)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Fx"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Radial;
                half _SrcBlend;
                half _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            Varyings vert (Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half a;
                if (_Radial > 0.5)
                {
                    half d = length(i.uv * 2.0 - 1.0);
                    a = saturate(1.0 - d);
                    a *= a;
                }
                else
                {
                    // луч: ярче у окна (uv.y = 1), мягкие края по ширине
                    a = i.uv.y * i.uv.y * sin(saturate(i.uv.x) * 3.14159);
                }
                half4 c = _Color * i.color;
                c.a *= a;
                // для аддитивного режима (One, One) заранее умножаем цвет на прозрачность
                if (_SrcBlend < 1.5 && _DstBlend < 1.5) c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }
}
