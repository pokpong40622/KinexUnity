// Trench wall for GroundCrackFX: unlit vertical gradient — near-black at the
// rim fading into a warm HDR glow that saturates toward the trench bottom, so
// the light reads as coming from deep underground and the rim stays a thin,
// clean lit edge instead of a flat emissive wall. uv.y: 0 = rim, 1 = bottom.
// _GlowMul is driven per-frame by GroundCrackFX.Flicker (SetGlow pulse + simmer).
// Falloff/MidGlow tuned in play mode 2026-07-18 against the reference image.
Shader "Collapse/CrackWall"
{
    Properties
    {
        _TopColor ("Top Color", Color) = (0.028, 0.024, 0.022, 1)
        _GlowColor ("Glow Color", Color) = (2.6, 1.05, 0.22, 1)
        _MidGlow ("Mid Glow", Range(0, 1)) = 0.09
        _Falloff ("Falloff", Float) = 3.4
        _GlowMul ("Glow Multiplier", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
            half4 _TopColor;
            half4 _GlowColor;
            half _MidGlow;
            half _Falloff;
            half _GlowMul;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half v = saturate(i.uv.y);
                // sharp pow term pools the glow at the bottom; the small v^2
                // bounce keeps the mid-wall from going dead black so the crack
                // still reads at grazing angles
                half g = pow(v, _Falloff) + _MidGlow * v * v;
                // slow brightness wander along the length so the seam glow is
                // not a uniform band (uv.x = normalized position along crack)
                g *= 0.82h + 0.18h * sin(i.uv.x * 21.7h + 1.3h);
                half3 col = _TopColor.rgb + _GlowColor.rgb * g * _GlowMul;
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
