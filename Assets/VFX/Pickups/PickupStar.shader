Shader "Lithomachia/Pickup Star"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _Size ("World size", Float) = .55
        _Brightness ("Brightness", Range(0,4)) = 1.8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Size;
            half _Brightness;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                // World-size billboard: rotating/falling/nonuniformly scaled items cannot squash the glint.
                float3 centerWS = TransformObjectToWorld(float3(0,0,0));
                float3 rightWS = UNITY_MATRIX_V[0].xyz, upWS = UNITY_MATRIX_V[1].xyz;
                float3 positionWS = centerWS + (rightWS * input.positionOS.x + upWS * input.positionOS.y) * _Size;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = abs(input.uv * 2 - 1);
                half vertical = exp(-p.x * p.x * 650) * pow(saturate(1 - p.y), 1.3);
                half horizontal = exp(-p.y * p.y * 650) * pow(saturate(1 - p.x), 1.3);
                half core = pow(saturate(1 - sqrt(p.x) - sqrt(p.y)), .65);
                half halo = exp(-dot(p,p) * 12) * .22;
                return half4(_Color.rgb * _Brightness, saturate(vertical + horizontal + core + halo) * _Color.a);
            }
            ENDHLSL
        }
    }
}
