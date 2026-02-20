Shader "Platformer/TileInstanced"
{
    Properties
    {
        _Color     ("Tile Color",      Color) = (0.22, 0.47, 0.82, 1)
        _EdgeColor ("Edge/Grid Color", Color) = (0.08, 0.16, 0.35, 1)
        _EdgeWidth ("Edge Width", Range(0, 0.12)) = 0.04
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                uint instanceID   : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            // Per-material properties (same for all tiles; no per-instance variation needed)
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EdgeColor;
                float  _EdgeWidth;
            CBUFFER_END

            StructuredBuffer<float4x4> _TileMatrixBuffer;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                // Read world transform from compute buffer
                float4x4 mat = _TileMatrixBuffer[IN.instanceID];
                
                float3 worldPos = mul(mat, float4(IN.positionOS.xyz, 1.0)).xyz;
                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv   = IN.uv;
                float2 edge = step(uv, _EdgeWidth) + step(1.0 - _EdgeWidth, uv);
                return lerp(_Color, _EdgeColor, saturate(edge.x + edge.y));
            }
            ENDHLSL
        }
    }
}
