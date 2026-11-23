Shader "Creek Diorama/Portal"
{
    Properties { _Color("Portal turquoise",Color)=(.02,.7,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+20" }
        Pass
        {
            // The disc is cut out of a quad. Alpha-to-coverage turns the one-pixel soft alpha at the
            // rim into MSAA sample coverage, so the cut-out edge is anti-aliased like a mesh edge.
            Cull Off ZWrite On AlphaToMask On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CreekAnalyticAA.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct A { float4 positionOS:POSITION;float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i){V o;UNITY_SETUP_INSTANCE_ID(i);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
            half4 frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p=i.uv*2-1;float r=length(p);
                // Derivative taken before the discard.
                float coverage=saturate((1-r)/max(fwidth(r),.00001)+.5);
                float angle=atan2(p.y,p.x),t=_Time.y;
                float spiral=CreekSilkPulse(r*32-angle*3-t*3);
                float rim=CreekAASmooth(.83,.99,r);
                half3 color=lerp(half3(.008,.035,.10),_Color.rgb,.12+spiral*.34+rim*.7);
                color+=half3(.45,.9,1)*pow(rim,6)*.65;
                // All derivatives precede the discard, including filtered spiral/rim.
                clip(coverage-.001);
                return half4(color,coverage);
            }
            ENDHLSL
        }
    }
}
