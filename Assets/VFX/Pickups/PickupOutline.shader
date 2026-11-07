Shader "Lithomachia/Pickup Outline"
{
    Properties
    {
        _Color ("Outline colour", Color) = (1,1,1,.32)
        _Width ("Outline width (pixels)", Range(.5,4)) = 1.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Width;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 normalCS = mul(UNITY_MATRIX_VP, float4(normalWS, 0));
                float2 direction = normalCS.xy * positionCS.w - positionCS.xy * normalCS.w;
                direction *= rsqrt(max(dot(direction, direction), 1e-8));
                positionCS.xy += direction * (2 * _Width / _ScaledScreenParams.xy) * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
