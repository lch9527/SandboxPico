Shader "Creek Diorama/Contact Foam"
{
    Properties { _Color("Foam ivory",Color)=(.82,.94,.89,.42) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-10" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "CreekClock.hlsl"
            #include "CreekAnalyticAA.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float2 position:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A v) { V o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=TransformObjectToHClip(v.vertex.xyz);o.uv=v.uv;o.position=v.vertex.xz;return o; }
            half4 frag(V i):SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float noise=.5+.25*sin(i.position.x*73+sin(i.position.y*49)+CreekTime()*.32)+.25*sin(i.position.y*91-i.position.x*41-CreekTime()*.24);
                float phase=i.uv.y*10-CreekTime()*.65+noise;
                float band=CreekFilteredSin(phase)*.5+.5;
                float shore=(1-smoothstep(.05,.45+noise*.2,i.uv.y))*(.45+.55*noise);
                float ripple=pow(band,3)*CreekPhaseVisibility(phase)*smoothstep(0,.13,i.uv.y)*(1-smoothstep(.4,1,i.uv.y))*.38;
                return half4(_Color.rgb,_Color.a*saturate(shore+ripple));
            }
            ENDHLSL
        }
    }
}
