Shader "Mech/BoostGlow"
{
    Properties { _Color ("Tint", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Output { float4 position : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            fixed4 _Color;
            Output vert(Input v)
            {
                Output o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.uv = v.uv;
                return o;
            }
            fixed4 frag(Output i) : SV_Target
            {
                float edge = saturate(1.0 - length(i.uv * 2.0 - 1.0));
                return fixed4(i.color.rgb, i.color.a * edge * edge);
            }
            ENDCG
        }
    }
}
