Shader "Platformer/PlayerInstanced"
{
    Properties
    {
        _BodyColor    ("Body Color",    Color) = (0.95, 0.35, 0.22, 1)
        _OutlineColor ("Outline Color", Color) = (0.12, 0.06, 0.04, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.2)) = 0.08
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }

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

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor;
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            StructuredBuffer<float4x4> _EntityMatrixBuffer;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4x4 mat = _EntityMatrixBuffer[IN.instanceID];
                
                float3 worldPos = mul(mat, float4(IN.positionOS.xyz, 1.0)).xyz;
                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.uv          = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv       = IN.uv;
                float2 centered = uv * 2.0 - 1.0; // [-1, 1]

                // Rounded rectangle SDF
                float r   = _OutlineWidth;
                float2 q  = abs(centered) - (1.0 - r);
                float sdf = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;

                float body    = 1.0 - step(0.0, sdf);
                float outline = step(0.0, sdf) * (1.0 - step(_OutlineWidth * 0.5, sdf));

                // Eye dots
                float eyeL = 1.0 - step(0.10, length(centered - float2(-0.30, 0.25)));
                float eyeR = 1.0 - step(0.10, length(centered - float2( 0.30, 0.25)));
                float eyes  = saturate(eyeL + eyeR);

                clip(body + outline - 0.001);

                return _BodyColor    * body
                     + _OutlineColor * outline
                     + half4(0.04, 0.02, 0.02, 1) * eyes * body;
            }
            ENDHLSL
        }
    }
}
