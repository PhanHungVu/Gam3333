Shader "Golem/Joint Energy"
{
    Properties
    {
        [HDR] _Tint ("Energy Tint", Color) = (0.08, 0.65, 1.6, 1)
        _Ring ("Ring Shape", Range(0, 1)) = 0
        _Segments ("Ring Segments", Float) = 6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Attributes { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            float4 _Tint;
            float _Ring, _Segments;
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Tint;
                output.uv = input.uv;
                return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radius = length(p);
                float falloff = saturate(1.0 - radius);
                float glow = falloff * falloff * falloff;
                float band = exp2(-abs(radius - 0.77) * 100.0);
                float halo = exp2(-abs(radius - 0.77) * 23.0) * 0.22;
                float angle = atan2(p.y, p.x) / 6.2831853 + 0.5;
                float phase = frac(angle * _Segments);
                float segment = smoothstep(0.08, 0.18, phase) * (1.0 - smoothstep(0.88, 0.98, phase));
                float ring = (band + halo) * segment * saturate((1.0 - radius) * 15.0);
                float opacity = _Ring > 0.5 ? ring : glow;
                return float4(input.color.rgb, saturate(input.color.a * opacity));
            }
            ENDCG
        }
    }
}
