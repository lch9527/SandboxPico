Shader "Creek Diorama/Lightweight Range Frame"
{
    Properties { _Color("Frame color",Color)=(.12,.72,.78,1) _CornerColor("Corner color",Color)=(.65,.95,.94,1) _Radius("Half thickness in world metres",Float)=.0012 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color, _CornerColor;
            float _Radius;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            // Still opaque, textureless bars (see the tests): the look comes from proportion and a little light.
            // Corners read as brackets, a touch thicker and brighter; the lines between them are thinner and
            // dim with height, so the box feels grounded and light rather than like a debug wireframe; and a
            // soft band of light climbs the frame every few seconds to say "this space is live".
            struct V { float4 positionCS:SV_POSITION; half3 color:COLOR; half2 frame:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V Vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 scale=float3(length(GetObjectToWorldMatrix()._m00_m10_m20),length(GetObjectToWorldMatrix()._m01_m11_m21),length(GetObjectToWorldMatrix()._m02_m12_m22));
                // Keep the bar at least about 1.5 pixels wide on screen. Thinner than a pixel it breaks into
                // dashes, because MSAA can only cover whole samples; widening with distance keeps it continuous.
                float viewDepth=abs(TransformWorldToView(TransformObjectToWorld(i.positionOS.xyz)).z);
                float pixel=viewDepth*2/(abs(UNITY_MATRIX_P._m11)*_ScreenParams.y);
                half corner=i.color.r;
                float radius=max(_Radius*lerp(.7,1.3,corner),pixel*lerp(.6,.9,corner));
                float3 position=i.positionOS.xyz+i.normalOS*radius/max(scale,1e-5);
                o.positionCS=TransformObjectToHClip(position);
                o.color=lerp(_Color.rgb,_CornerColor.rgb,corner);
                // x: height in the box, 0 at the floor and 1 at the top. y: 1 on a corner bracket.
                o.frame=half2(saturate(i.positionOS.y+.5),corner);
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half height=i.frame.x, corner=i.frame.y;
                half3 colour=i.color*lerp(lerp(1.05,.72,height),1,corner);
                // The band enters below the floor and leaves above the top, with a pause between passes.
                half band=saturate(1-abs(frac(_Time.y*.16)*1.7-.35-height)*3.5); band*=band;
                colour=lerp(colour,_CornerColor.rgb*1.06,band*.6);
                colour*=1+.07*sin(_Time.y*1.7)*corner;
                return half4(colour,1);
            }
            ENDHLSL
        }
    }
}
