// Invisible depth-only mask (Gabriel Aguiar ground-crack technique): renders
// before the street opaques, writes depth but no colour, so anything at the
// normal Geometry queue behind it is culled — punching a crack-shaped hole in
// the ground that reveals the trench meshes drawn earlier (queue 1980-1981).
Shader "Collapse/DepthMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "DepthMask"
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
        }
    }
}