// Heat-shimmer ribbon over the lava seam: re-samples the camera opaque texture
// with a scrolling noise offset, so everything seen through the quad wavers.
// Draws BEFORE the additive glow strips (queue 2985) because the opaque texture
// holds no transparents — drawing later would erase them inside the quad.
// Requires the active URP asset to have Opaque Texture enabled; GroundCrackFX
// checks that at runtime and skips the ribbon when unsupported (Android tier).
Shader "Collapse/HeatDistortion"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        _Strength ("Strength", Float) = 0.012
        _Speed ("Scroll Speed", Float) = 1.4
        _NoiseScale ("Noise Scale", Float) = 3.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-15"
            "RenderPipeline" = "UniversalPipeline"
        }
        Pass
        {
            Blend One Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            float _Strength;
            float _Speed;
            float _NoiseScale;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 screenUV = IN.positionCS.xy / _ScaledScreenParams.xy;
                float2 nuv = IN.uv * _NoiseScale + float2(0.13, -1.0) * _Time.y * _Speed;
                float2 n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, nuv).rg - 0.5;
                // fade at the ribbon top (uv.y=1) and at the crack tips (vert alpha)
                float fade = (1.0 - IN.uv.y) * IN.color.a;
                float2 offset = n * _Strength * fade;
                half3 scene = SampleSceneColor(screenUV + offset);
                return half4(scene, 1.0);
            }
            ENDHLSL
        }
    }
}
